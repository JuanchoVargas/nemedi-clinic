// ============================================================
// use-nav-items.ts — Navegación lateral según el rol de la sesión
//
// La visibilidad sale de la matriz de permisos (src/lib/permissions.ts), igual
// que antes en el Header. El PlatformAdmin solo ve la plataforma.
// También alimenta el breadcrumb (ROUTE_LABELS).
// ============================================================
import { useMemo } from "react";
import {
  FileBarChart,
  FileUp,
  SlidersHorizontal,
  Banknote,
  Receipt,
  ReceiptText,
  Calendar,
  CalendarDays,
  ClipboardCheck,
  ClipboardList,
  Layers,
  LayoutDashboard,
  Package,
  Package2,
  Settings,
  Stethoscope,
  Users,
  UsersRound,
  type LucideIcon,
} from "lucide-react";
import type { LinkProps } from "@tanstack/react-router";
import { usePermissions } from "@/hooks/use-permissions";

/** Ruta tipada del router: un typo en `to` no compila. */
type RoutePath = NonNullable<LinkProps["to"]>;

export interface NavLink {
  label: string;
  to: RoutePath;
  icon: LucideIcon;
}

/** Un ítem es un enlace o un grupo desplegable (con hijos y sin ruta propia). */
export type NavItem = NavLink | { label: string; icon: LucideIcon; children: NavLink[] };

export interface NavSection {
  label: string;
  items: NavItem[];
}

export function useNavItems(): NavSection[] {
  const { can, isPlatformAdmin, role } = usePermissions();

  return useMemo(() => {
    if (!role) return [];
    if (isPlatformAdmin) {
      return [{ label: "Plataforma", items: [{ label: "Plataforma", to: "/platform", icon: Layers }] }];
    }

    const clinic: NavItem[] = [
      { label: "Dashboard", to: "/dashboard", icon: LayoutDashboard },
      { label: "Pacientes", to: "/patients", icon: UsersRound },
      { label: "Valoraciones", to: "/valoraciones", icon: ClipboardCheck },
      {
        label: "Calendario",
        icon: Calendar,
        children: [
          { label: "Vista calendario", to: "/calendar", icon: CalendarDays },
          { label: "Hoja del día", to: "/calendar/day-sheet", icon: ClipboardList },
        ],
      },
    ];

    const catalog: NavItem[] = [{ label: "Procedimientos", to: "/procedures", icon: Stethoscope }];
    if (can("packages.read")) catalog.push({ label: "Paquetes", to: "/packages", icon: Package });
    catalog.push({ label: "Inventario", to: "/inventory", icon: Package2 });

    const adminChildren: NavLink[] = [];
    if (can("users.update")) adminChildren.push({ label: "Usuarios", to: "/admin/users", icon: Users });
    // Datos de la clínica, sedes con horario, políticas e IVA: todo en Configuración
    if (can("branches.read")) adminChildren.push({ label: "Configuración", to: "/settings", icon: SlidersHorizontal });
    if (can("imports.run")) adminChildren.push({ label: "Importaciones", to: "/imports", icon: FileUp });

    const sections: NavSection[] = [
      { label: "Clínica", items: clinic },
      { label: "Catálogo", items: catalog },
    ];
    // Ventas y caja: recepción y dueño. La esteticista no las ve.
    const finance: NavItem[] = [];
    if (can("sales.read")) finance.push({ label: "Ventas", to: "/sales", icon: Receipt });
    if (can("sales.read")) finance.push({ label: "Facturas", to: "/invoices", icon: ReceiptText });
    if (can("cash.operate")) finance.push({ label: "Caja", to: "/cash", icon: Banknote });
    if (can("reports.read")) finance.push({ label: "Reportes", to: "/reports", icon: FileBarChart });
    if (finance.length > 0) sections.push({ label: "Finanzas", items: finance });
    if (adminChildren.length > 0) {
      sections.push({
        label: "Gestión",
        items: [{ label: "Administración", icon: Settings, children: adminChildren }],
      });
    }
    return sections;
  }, [can, isPlatformAdmin, role]);
}

/** Etiquetas del breadcrumb por segmento de ruta. Un id (GUID) se muestra como "Detalle". */
export const ROUTE_LABELS: Record<string, string> = {
  dashboard: "Dashboard",
  patients: "Pacientes",
  calendar: "Calendario",
  "day-sheet": "Hoja del día",
  procedures: "Procedimientos",
  packages: "Paquetes",
  inventory: "Inventario",
  valoraciones: "Valoraciones",
  admin: "Administración",
  users: "Usuarios",
  branches: "Sedes",
  super: "Administración",
  tenants: "Mi clínica",
  platform: "Plataforma",
  "change-password": "Cambiar contraseña",
  sales: "Ventas",
  invoices: "Facturas",
  cash: "Caja",
  settings: "Configuración",
  reports: "Reportes",
  imports: "Importaciones",
  import: "Importar",
  new: "Nuevo",
  edit: "Editar",
};
