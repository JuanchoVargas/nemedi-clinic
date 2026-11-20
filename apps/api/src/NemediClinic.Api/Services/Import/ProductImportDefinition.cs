using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NemediClinic.Application.Interfaces;
using NemediClinic.Domain.Entities;
using NemediClinic.Domain.Enums;
using NemediClinic.Infrastructure.Persistence;

namespace NemediClinic.Api.Services.Import;

/// <summary>
/// Importar productos y, si el archivo los trae, su stock por lote. Reglas:
///   · Un producto se reconoce por su referencia (SKU) si la fila la trae, si no por su nombre. Varias
///     filas del mismo producto son UN producto con varios lotes (los datos del producto salen de la
///     primera fila que los trae; si otra trae uno distinto se avisa).
///   · Lote = cantidad (+ número y vencimiento, opcionales). Cada lote entra como en Inventario →
///     Registrar entrada: lote, entrada de inventario y movimiento, y el stock se recalcula desde los
///     lotes (AfterSaveAsync), nunca se suma a mano.
///   · Un medicamento o dispositivo médico necesita número de lote y vencimiento para cargar stock
///     (igual que el formulario): sin ellos el producto se crea sin existencias y se avisa.
///   · Sin columna de cantidad: productos sin existencias, aviso general.
///   · Duplicados: con Omitir el producto y sus lotes se dejan; con Actualizar cambian los datos que trae
///     la fila y se agregan sus lotes, salvo un número de lote que el producto ya tiene (no se suma dos veces).
/// </summary>
public class ProductImportDefinition(AppDbContext db, ProductLotService lots, IClock clock) : IImportDefinition
{
    public string Entidad => "products";
    public string Nombre => "Productos";

    private const string LotGroup = "Lote y existencias";
    private static readonly string[] LotKeys = ["cantidad", "numeroLote", "vencimiento", "proveedor", "factura"];
    private readonly HashSet<Guid> _touched = [];

    public Task<IReadOnlyList<ImportField>> GetFieldsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ImportField>>(Fields());

    public static List<ImportField> Fields() =>
    [
        new("nombre", "Nombre", ImportFieldType.Text, MaxLength: 200,
            Aliases: ["nombre", "producto", "nombre del producto", "nombre producto", "articulo", "item", "insumo"],
            Hint: "Obligatorio para un producto nuevo."),
        new("referencia", "Referencia / SKU", ImportFieldType.Code, MaxLength: 100,
            Aliases: ["sku", "referencia", "ref", "codigo", "cod", "codigo producto", "cod producto", "codigo de barras", "ean", "plu", "referencia del producto"]),
        new("descripcion", "Descripción", ImportFieldType.Text, MaxLength: 1000, Aliases: ["descripcion", "detalle"]),
        new("categoria", "Categoría", ImportFieldType.Text, MaxLength: ProductCategories.MaxLength,
            Aliases: ["categoria", "categoria del producto", "linea", "linea de producto", "familia", "grupo"],
            Hint: "Texto libre (Facial, Corporal…). Si ya existe con otras mayúsculas, se usa la que ya tienes."),
        new("uso", "Uso", ImportFieldType.Option, Options: Usos,
            Aliases: ["uso", "tipo de producto", "tipo producto", "destino", "se vende"],
            Hint: "Venta, insumo de cabina o ambos. Vacío: venta si tiene precio, si no insumo de cabina."),
        new("unidad", "Unidad de medida", ImportFieldType.Text, MaxLength: 20,
            Aliases: ["unidad", "unidad de medida", "um", "u m", "presentacion", "medida"], Hint: "Vacío = unidad."),
        new("precioVenta", "Precio de venta", ImportFieldType.Money,
            Aliases: ["precio", "precio de venta", "precio venta", "valor", "valor venta", "pvp", "precio al publico", "precio publico"]),
        new("iva", "IVA", ImportFieldType.Iva,
            Aliases: ["iva", "iva %", "porcentaje iva", "tarifa iva", "impuesto"], Hint: "0, 5 o 19. Vacío = el de la clínica."),
        new("tipoRegulatorio", "Clasificación regulatoria", ImportFieldType.Option, Options: Clasificaciones,
            Aliases: ["clasificacion", "clasificacion regulatoria", "tipo regulatorio", "tipo", "clase", "categoria invima"],
            Hint: "Medicamento, dispositivo médico, insumo o cosmético. Vacío = insumo."),
        new("registroInvima", "Registro INVIMA", ImportFieldType.Text, MaxLength: 60,
            Aliases: ["registro invima", "invima", "registro sanitario", "reg invima", "reg sanitario", "rs"]),
        new("principioActivo", "Principio activo", ImportFieldType.Text, MaxLength: 150, Aliases: ["principio activo", "molecula", "componente"]),
        new("concentracion", "Concentración", ImportFieldType.Text, MaxLength: 60, Aliases: ["concentracion", "dosis"]),
        new("cadenaFrio", "Cadena de frío", ImportFieldType.Boolean, Aliases: ["cadena de frio", "refrigerado", "refrigeracion", "nevera"]),
        new("stockMinimo", "Stock mínimo", ImportFieldType.Money, Aliases: ["stock minimo", "minimo", "stock min", "cantidad minima", "punto de pedido"]),
        new("cantidad", "Cantidad (stock inicial)", ImportFieldType.Quantity, Group: LotGroup,
            Aliases: ["cantidad", "stock", "stock inicial", "existencias", "unidades", "cant", "inventario", "saldo", "stock actual"],
            Hint: "Sin cantidad el producto queda sin existencias."),
        new("numeroLote", "Número de lote", ImportFieldType.Text, MaxLength: 60, Group: LotGroup,
            Aliases: ["lote", "numero de lote", "no lote", "nro lote", "n lote", "num lote", "lote no"]),
        new("vencimiento", "Fecha de vencimiento", ImportFieldType.Date, Expiry: true, Group: LotGroup,
            Aliases: ["vencimiento", "fecha de vencimiento", "fecha vencimiento", "vence", "fecha venc", "f venc", "f vencimiento", "caducidad", "expiracion", "fv"],
            Hint: "Acepta mes y año (03/2027): se toma el último día del mes."),
        new("proveedor", "Proveedor", ImportFieldType.Text, MaxLength: 150, Group: LotGroup, Aliases: ["proveedor", "distribuidor", "laboratorio"]),
        new("costo", "Costo unitario", ImportFieldType.Money, Group: LotGroup,
            Aliases: ["costo", "costo unitario", "costo por unidad", "costo de compra", "precio de compra", "valor compra", "valor de compra", "precio compra"],
            Hint: "Lo que costó cada unidad de este lote. El producto muestra el del lote más reciente."),
        new("factura", "Factura de compra", ImportFieldType.Text, MaxLength: 60, Group: LotGroup,
            Aliases: ["factura", "numero de factura", "no factura", "factura de compra", "nro factura"]),
    ];

    public IReadOnlyList<string> Notices(IReadOnlyCollection<string> mappedKeys)
    {
        if (!mappedKeys.Contains("cantidad"))
            return ["El archivo no trae cantidades: los productos se crean sin existencias. El stock se carga aparte, en Inventario → Registrar entrada, con su lote y vencimiento."];
        if (!mappedKeys.Contains("numeroLote") || !mappedKeys.Contains("vencimiento"))
            return ["Sin número de lote o sin vencimiento: los insumos y cosméticos entran con lo que traigan; los medicamentos y dispositivos médicos se crean sin existencias, porque su stock necesita lote y vencimiento."];
        return [];
    }

    // ── Grupos: un producto del archivo y sus filas ──

    private sealed class Group
    {
        public required string Label { get; init; }
        public string? Referencia { get; set; }
        public string NameKey { get; set; } = "";
        public List<ImportRow> Rows { get; } = [];
        public Guid? ExistingId { get; set; }
        public string? ExistingLabel { get; set; }
    }

    private List<Group> _groups = [];
    private List<string> _categories = [];
    private Dictionary<Guid, (RegulatoryType Tipo, HashSet<string> Lotes)> _existingInfo = [];

    private static List<Group> BuildGroups(IReadOnlyList<ImportRow> rows)
    {
        var groups = new List<Group>();
        var bySku = new Dictionary<string, Group>();
        var byName = new Dictionary<string, Group>();
        foreach (var row in rows)
        {
            var sku = row.Text("referencia");
            var name = row.Text("nombre");
            if (sku is null && name is null)
            {
                row.Errors.Add("Falta el nombre o la referencia del producto.");
                continue;
            }
            var nameKey = name is null ? "" : ImportText.Key(name);
            Group? group = null;
            if (sku is not null) bySku.TryGetValue(sku, out group);
            // Sin referencia: el producto del archivo con ese nombre (con o sin referencia)
            if (group is null && sku is null) byName.TryGetValue(nameKey, out group);
            if (group is null)
            {
                group = new Group { Label = name ?? sku!, Referencia = sku, NameKey = nameKey };
                groups.Add(group);
                if (sku is not null) bySku[sku] = group;
                if (nameKey.Length > 0) byName.TryAdd(nameKey, group);
            }
            else if (group.NameKey.Length == 0 && nameKey.Length > 0)
            {
                group.NameKey = nameKey;
                byName.TryAdd(nameKey, group);
            }
            group.Rows.Add(row);
        }
        return groups;
    }

    public async Task ValidateAsync(IReadOnlyList<ImportRow> rows, CancellationToken ct)
    {
        _groups = BuildGroups(rows);
        _categories = await db.Products.AsNoTracking().Where(p => p.Categoria != null).Select(p => p.Categoria!).Distinct().ToListAsync(ct);
        var products = await db.Products.AsNoTracking()
            .Select(p => new { p.Id, p.Nombre, p.Referencia, p.TipoRegulatorio }).ToListAsync(ct);
        var bySku = products.Where(p => !string.IsNullOrWhiteSpace(p.Referencia))
            .GroupBy(p => p.Referencia!.Trim().Replace(" ", "").ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First());
        var byName = products.GroupBy(p => ImportText.Key(p.Nombre)).ToDictionary(g => g.Key, g => g.First());

        foreach (var group in _groups)
        {
            if (group.Referencia is not null)
            {
                if (bySku.TryGetValue(group.Referencia, out var found)) (group.ExistingId, group.ExistingLabel) = (found.Id, found.Nombre);
                else if (group.NameKey.Length > 0 && byName.TryGetValue(group.NameKey, out var sameName))
                    group.Rows[0].Warnings.Add($"Ya existe «{sameName.Nombre}»{(sameName.Referencia is null ? " sin referencia" : $" con la referencia {sameName.Referencia}")}: como esta fila trae otra referencia, se crea un producto nuevo.");
            }
            else if (byName.TryGetValue(group.NameKey, out var found))
            {
                (group.ExistingId, group.ExistingLabel) = (found.Id, found.Nombre);
            }
            foreach (var row in group.Rows)
            {
                row.ExistingId = group.ExistingId;
                row.ExistingLabel = group.ExistingLabel;
            }
        }

        var existingIds = _groups.Where(g => g.ExistingId is not null).Select(g => g.ExistingId!.Value).ToList();
        var existingLots = existingIds.Count == 0
            ? []
            : await db.ProductLots.AsNoTracking().Where(l => existingIds.Contains(l.ProductId) && l.NumeroLote != null)
                .Select(l => new { l.ProductId, l.NumeroLote }).ToListAsync(ct);
        _existingInfo = products.Where(p => existingIds.Contains(p.Id)).ToDictionary(p => p.Id,
            p => (p.TipoRegulatorio, existingLots.Where(l => l.ProductId == p.Id).Select(l => ImportText.Key(l.NumeroLote)).ToHashSet()));

        foreach (var group in _groups) ValidateGroup(group);
    }

    private void ValidateGroup(Group group)
    {
        // Datos del producto: de la primera fila que los trae (y aviso a la que traiga otro)
        var valid = group.Rows.Where(r => r.Errors.Count == 0).ToList();
        if (valid.Count == 0) return;
        foreach (var f in Fields().Where(f => f.Group is null && f.Key is not "nombre" and not "referencia"))
            ImportGrouping.Merge(valid, f.Key, f.Label);
        if (group.ExistingId is null && group.NameKey.Length == 0)
            foreach (var row in valid) row.Errors.Add("Falta el nombre del producto (es nuevo: con la referencia sola no se puede crear).");

        var tipo = Enum.TryParse<RegulatoryType>(valid.Select(r => r.Text("tipoRegulatorio")).FirstOrDefault(x => x is not null), out var parsed) ? parsed
            : group.ExistingId is { } id && _existingInfo.TryGetValue(id, out var info) ? info.Tipo
            : RegulatoryType.Insumo;
        var existingLots = group.ExistingId is { } eid && _existingInfo.TryGetValue(eid, out var ei) ? ei.Lotes : [];
        var lotRows = new Dictionary<string, int>();

        foreach (var row in group.Rows.Where(r => r.Errors.Count == 0))
        {
            var cantidad = row.Get<decimal>("cantidad");
            var numero = row.Text("numeroLote");
            var vence = row.Get<DateOnly>("vencimiento");
            if (cantidad is null)
            {
                // Proveedor o factura solos no son un lote (pueden venir en un listado sin existencias)
                if (numero is not null || vence is not null)
                    row.Errors.Add("El lote no trae cantidad.");
                else if (row.Get<decimal>("costo") is not null)
                    row.Warnings.Add("El costo va con el lote: sin cantidad no se guarda.");
                continue;
            }
            if (tipo is RegulatoryType.Medicamento or RegulatoryType.DispositivoMedico && (numero is null || vence is null))
            {
                row.Warnings.Add($"Un {(tipo == RegulatoryType.Medicamento ? "medicamento" : "dispositivo médico")} necesita número de lote y vencimiento para cargar stock: esta cantidad no se carga (hazlo en Inventario → Registrar entrada).");
                row.Values.Remove("cantidad");
                continue;
            }
            if (numero is not null)
            {
                var key = ImportText.Key(numero);
                if (lotRows.TryGetValue(key, out var first))
                {
                    row.Errors.Add($"El lote {numero} de este producto se repite (también en la fila {first}).");
                    continue;
                }
                lotRows[key] = row.Fila;
                if (existingLots.Contains(key))
                {
                    row.Warnings.Add($"El producto ya tiene el lote {numero}: no se suma de nuevo.");
                    row.Values.Remove("cantidad");
                    continue;
                }
            }
            if (vence is { } v && v < clock.Today)
                row.Warnings.Add($"El lote {numero ?? "sin número"} venció el {v:dd/MM/yyyy}: queda registrado, pero no cuenta como stock.");
        }
    }

    public ImportPlan Plan(IReadOnlyList<ImportRow> rows)
    {
        var nuevos = _groups.Where(g => g.ExistingId is null && g.Rows.Any(r => r.Status == ImportRowStatus.Valida)).ToList();
        var existentes = _groups.Count(g => g.ExistingId is not null && g.Rows.Any(r => r.Status == ImportRowStatus.Duplicada));
        var lotes = nuevos.SelectMany(g => g.Rows).Where(r => r.Status == ImportRowStatus.Valida && r.Get<decimal>("cantidad") is not null).ToList();
        var detalle = lotes.Count == 0
            ? "Sin existencias: el stock se carga aparte."
            : $"{lotes.Count} {(lotes.Count == 1 ? "lote" : "lotes")} con {Units(lotes.Sum(r => r.Get<decimal>("cantidad")!.Value))} unidades para los productos nuevos.";
        return new ImportPlan(nuevos.Count, existentes, detalle);
    }

    public async Task<ImportApplyResult> ApplyAsync(IReadOnlyList<ImportRow> rows, ImportApplyContext context, CancellationToken ct)
    {
        int creadas = 0, actualizadas = 0, omitidas = 0, lotesCreados = 0;
        decimal unidades = 0;
        var ids = _groups.Where(g => g.ExistingId is not null).Select(g => g.ExistingId!.Value).ToList();
        var tracked = context.Mode == DuplicateMode.Actualizar && ids.Count > 0
            ? await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct)
            : [];

        foreach (var group in _groups)
        {
            Product? product;
            List<ImportRow> usable;
            if (group.ExistingId is null)
            {
                usable = group.Rows.Where(r => r.Status == ImportRowStatus.Valida).ToList();
                if (usable.Count == 0) continue;
                product = new Product { Nombre = usable.Select(r => r.Text("nombre")).First(n => n is not null)!, Referencia = group.Referencia };
                Apply(product, usable, isNew: true);
                db.Products.Add(product);
                creadas++;
            }
            else
            {
                usable = group.Rows.Where(r => r.Status == ImportRowStatus.Duplicada).ToList();
                if (usable.Count == 0) continue;
                if (!tracked.TryGetValue(group.ExistingId.Value, out product))
                {
                    omitidas++;
                    continue;
                }
                Apply(product, usable, isNew: false);
                actualizadas++;
            }

            foreach (var row in usable)
            {
                if (row.Get<decimal>("cantidad") is not { } cantidad) continue;
                var lote = lots.CreateLot(product, cantidad, row.Text("numeroLote"), row.Get<DateOnly>("vencimiento"),
                    row.Text("proveedor"), row.Text("factura"), context.Now, row.Get<decimal>("costo"));
                var entry = new InventoryEntry
                {
                    ProductId = product.Id, Cantidad = cantidad, MotivoEntrada = EntryReason.Ajuste,
                    Observacion = "Carga inicial importada desde archivo", UserId = context.UserId, FechaEntrada = context.Now,
                    ProductLotId = lote.Id,
                };
                db.InventoryEntries.Add(entry);
                db.InventoryMovements.Add(new InventoryMovement
                {
                    ProductId = product.Id, Cantidad = cantidad, TipoMovimiento = MovementType.Entrada,
                    Referencia = $"Carga inicial (importación) #{entry.Id.ToString()[..8]}", UserId = context.UserId,
                    FechaMovimiento = context.Now, ProductLotId = lote.Id,
                });
                lotesCreados++;
                unidades += cantidad;
            }
            if (usable.Any(r => r.Get<decimal>("cantidad") is not null)) _touched.Add(product.Id);
        }
        var detalle = lotesCreados == 0 ? null : $"{lotesCreados} {(lotesCreados == 1 ? "lote" : "lotes")} con {Units(unidades)} unidades";
        return new ImportApplyResult(creadas, actualizadas, omitidas, detalle);
    }

    /// <summary>El stock sale de los lotes ya guardados (no vencidos), como en una entrada manual.</summary>
    public async Task AfterSaveAsync(CancellationToken ct)
    {
        foreach (var id in _touched) await lots.RecalculateStockAsync(id, ct);
    }

    /// <summary>Al actualizar solo cambia lo que traen las filas; nombre y referencia no (son la clave).</summary>
    private void Apply(Product p, List<ImportRow> rows, bool isNew)
    {
        T? First<T>(string key) where T : struct => rows.Select(r => r.Get<T>(key)).FirstOrDefault(v => v is not null);
        string? Text(string key) => rows.Select(r => r.Text(key)).FirstOrDefault(v => v is not null);

        if (Text("descripcion") is { } descripcion) p.Descripcion = descripcion;
        // "facial" y "Facial" son la misma: la ya usada en la clínica, o la primera del archivo
        if (ProductCategories.Normalize(Text("categoria"), _categories) is { } categoria)
        {
            p.Categoria = categoria;
            if (!_categories.Contains(categoria)) _categories.Add(categoria);
        }
        if (Text("unidad") is { } unidad) p.UnidadMedida = unidad;
        else if (isNew) p.UnidadMedida = "unidad";
        if (First<decimal>("precioVenta") is { } precio) p.PrecioVenta = precio;
        if (First<decimal>("iva") is { } iva) p.IvaPorcentaje = iva;
        if (Enum.TryParse<ProductType>(Text("uso"), out var uso)) p.TipoProducto = uso;
        else if (isNew) p.TipoProducto = p.PrecioVenta > 0 ? ProductType.Venta : ProductType.InsumoCabina;
        if (Enum.TryParse<RegulatoryType>(Text("tipoRegulatorio"), out var tipo)) p.TipoRegulatorio = tipo;
        if (Text("registroInvima") is { } invima) p.RegistroSanitarioInvima = invima;
        if (Text("principioActivo") is { } principio) p.PrincipioActivo = principio;
        if (Text("concentracion") is { } concentracion) p.Concentracion = concentracion;
        if (First<bool>("cadenaFrio") is { } frio) p.RequiereCadenaFrio = frio;
        if (First<decimal>("stockMinimo") is { } minimo) p.StockMinimo = minimo;
    }

    private static string Units(decimal value) => value.ToString(value == decimal.Truncate(value) ? "#,0" : "#,0.##", CultureInfo.GetCultureInfo("es-CO"));

    private static readonly Dictionary<string, string> Usos = ImportOptions.Build(
        ("Venta", ["venta", "para venta", "se vende", "si"]),
        ("InsumoCabina", ["insumo", "insumo de cabina", "cabina", "uso interno", "consumo", "consumo interno", "no"]),
        ("Ambos", ["ambos", "venta y cabina", "venta e insumo", "mixto"]));

    private static readonly Dictionary<string, string> Clasificaciones = ImportOptions.Build(
        ("Medicamento", ["medicamento", "med", "farmaco"]),
        ("DispositivoMedico", ["dispositivo medico", "dispositivo", "dm", "dispositivo medico de uso"]),
        ("Insumo", ["insumo", "material", "consumible"]),
        ("Cosmetico", ["cosmetico", "cosmeticos", "dermocosmetico"]));
}
