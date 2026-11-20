// ============================================================
// router/index.tsx — Árbol de rutas (Nemedi Clinic)
//
// Estructura:
//   root (RootLayout: Header + Outlet + Footer)
//   ├── /              → HomePage (pública)
//   ├── /login         → LoginPage (pública)
//   └── _protected     → guard via beforeLoad
//       ├── /dashboard          → DashboardPage
//       ├── /patients           → PatientsPage
//       ├── /patients/new       → PatientFormPage (modo crear)
//       ├── /patients/import    → PatientImportPage (Admin y SuperAdmin)
//       ├── /patients/$id       → PatientDetailPage
//       ├── /patients/$id/edit  → PatientFormPage (modo editar)
//       └── /platform           → PlatformPage (solo PlatformAdmin; es su ÚNICA ruta)
//
// EQUIVALENTE A: router/index.js de SINERGIA con beforeEach guards
// PATTERNS.md sección: "Routing"
// ============================================================

import {
  createRouter,
  createRootRoute,
  createRoute,
  lazyRouteComponent,
  redirect,
} from "@tanstack/react-router";
import { TanStackRouterDevtools } from "@tanstack/router-devtools";
import { SHOW_DEVTOOLS } from "@/lib/devtools";
import { z } from "zod";

import { RootLayout } from "@/components/layout/RootLayout";
import { PLATFORM_ADMIN } from "@/lib/permissions";
import { useAuthStore } from "@/stores/auth.store";
import { refreshSession } from "@/lib/auth-session";
import { useToastStore } from "@/stores/toast.store";

// ─── Páginas con carga diferida ────────────────────────────
// Cada ruta protegida es su propio chunk: el login no descarga FullCalendar, Recharts,
// la firma ni los formularios de venta. También la portada y el login: la carga inicial es
// solo el router, el cliente HTTP y el layout.
const HomePage = lazyRouteComponent(() => import("@/pages/HomePage"), "HomePage");
const LoginPage = lazyRouteComponent(() => import("@/pages/LoginPage"), "LoginPage");
const DashboardPage = lazyRouteComponent(() => import("@/pages/DashboardPage"), "DashboardPage");
const PatientsPage = lazyRouteComponent(() => import("@/pages/PatientsPage"), "PatientsPage");
const PatientFormPage = lazyRouteComponent(() => import("@/pages/PatientFormPage"), "PatientFormPage");
const PatientDetailPage = lazyRouteComponent(() => import("@/pages/PatientDetailPage"), "PatientDetailPage");
const CalendarPage = lazyRouteComponent(() => import("@/pages/CalendarPage"), "CalendarPage");
const DaySheetPage = lazyRouteComponent(() => import("@/pages/DaySheetPage"), "DaySheetPage");
const ProceduresPage = lazyRouteComponent(() => import("@/pages/ProceduresPage"), "ProceduresPage");
const PackagesPage = lazyRouteComponent(() => import("@/pages/PackagesPage"), "PackagesPage");
const PackageFormPage = lazyRouteComponent(() => import("@/pages/PackageFormPage"), "PackageFormPage");
const PackageDetailPage = lazyRouteComponent(() => import("@/pages/PackageDetailPage"), "PackageDetailPage");
const InventoryPage = lazyRouteComponent(() => import("@/pages/InventoryPage"), "InventoryPage");
const UsersPage = lazyRouteComponent(() => import("@/pages/admin/UsersPage"), "UsersPage");
const SettingsPage = lazyRouteComponent(() => import("@/pages/SettingsPage"), "SettingsPage");
const ReportsPage = lazyRouteComponent(() => import("@/pages/ReportsPage"), "ReportsPage");
const PlatformPage = lazyRouteComponent(() => import("@/pages/platform/PlatformPage"), "PlatformPage");
const ChangePasswordPage = lazyRouteComponent(() => import("@/pages/ChangePasswordPage"), "ChangePasswordPage");
const ValuationsPage = lazyRouteComponent(() => import("@/pages/ValuationsPage"), "ValuationsPage");
const SalesPage = lazyRouteComponent(() => import("@/pages/SalesPage"), "SalesPage");
const InvoicesPage = lazyRouteComponent(() => import("@/pages/InvoicesPage"), "InvoicesPage");
const NewSalePage = lazyRouteComponent(() => import("@/pages/NewSalePage"), "NewSalePage");
const CashPage = lazyRouteComponent(() => import("@/pages/CashPage"), "CashPage");
const PatientImportPage = lazyRouteComponent(() => import("@/pages/PatientImportPage"), "PatientImportPage");
const ImportsPage = lazyRouteComponent(() => import("@/pages/ImportsPage"), "ImportsPage");
const ProductImportPage = lazyRouteComponent(() => import("@/pages/ProductImportPage"), "ProductImportPage");
const ProcedureImportPage = lazyRouteComponent(() => import("@/pages/ProcedureImportPage"), "ProcedureImportPage");
const PackageImportPage = lazyRouteComponent(() => import("@/pages/PackageImportPage"), "PackageImportPage");

// Guard por rol: usa el rol del JWT (auth store). Si el usuario no tiene
// un rol permitido, avisa con un toast y lo manda al dashboard.
function requireRoles(allowed: string[]) {
  const role = useAuthStore.getState().user?.role;
  if (!role || !allowed.includes(role)) {
    // deferError: en una carga directa de la URL el <Toaster /> aún no existe
    useToastStore.deferError("No tienes permisos para acceder a esa sección");
    throw redirect({ to: "/dashboard" });
  }
}

// ─── Root: layout persistente ──────────────────────────────
const rootRoute = createRootRoute({
  component: () => (
    <>
      <RootLayout />
      {SHOW_DEVTOOLS && <TanStackRouterDevtools position="bottom-right" />}
    </>
  ),
});

// ─── Rutas públicas ────────────────────────────────────────
const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/",
  component: HomePage,
});

const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/login",
  validateSearch: z.object({
    redirect: z.string().optional(),
  }),
  component: LoginPage,
});

// ─── Rutas protegidas (layout route con beforeLoad guard) ──
const protectedRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: "_protected",
  beforeLoad: async ({ location }) => {
    // JWT vencido pero con refresh token: se renueva antes de mandar a /login
    if (!useAuthStore.getState().isAuthenticated() && useAuthStore.getState().refreshToken) {
      await refreshSession();
    }
    const isAuth = useAuthStore.getState().isAuthenticated();
    if (!isAuth) {
      throw redirect({
        to: "/login",
        search: { redirect: location.href },
      });
    }
    // Clave temporal (usuario nuevo o restablecido): nada más que /change-password.
    // La API además responde 403 fuera de /auth/* mientras el JWT lleve ese bloqueo.
    const mustChange = useAuthStore.getState().user?.mustChangePassword;
    if (mustChange && location.pathname !== "/change-password") {
      throw redirect({ to: "/change-password" });
    }
    if (location.pathname === "/change-password") return;

    // El PlatformAdmin vive fuera de todo tenant: las pantallas de clínica le darían 403.
    const role = useAuthStore.getState().user?.role;
    if (role === PLATFORM_ADMIN && !location.pathname.startsWith("/platform")) {
      throw redirect({ to: "/platform" });
    }
  },
});

const dashboardRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/dashboard",
  component: DashboardPage,
});

const patientsRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/patients",
  component: PatientsPage,
});

// /patients/new debe declararse ANTES que /patients/$id para que
// TSR no lo capture como un id="new".
const patientNewRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/patients/new",
  component: PatientFormPage,
});

// Importar desde Excel/CSV (ImportsController: policy Admin). También ANTES de /patients/$id.
const patientImportRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/patients/import",
  beforeLoad: () => requireRoles(["SuperAdmin", "Admin"]),
  component: PatientImportPage,
});

const importsRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/imports",
  beforeLoad: () => requireRoles(["SuperAdmin", "Admin"]),
  component: ImportsPage,
});

const patientDetailRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/patients/$id",
  component: PatientDetailPage,
});

const patientEditRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/patients/$id/edit",
  component: PatientFormPage,
});

// /calendar/day-sheet ANTES que /calendar para coincidir exacto primero
const daySheetRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/calendar/day-sheet",
  component: DaySheetPage,
});

const calendarRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/calendar",
  // Precarga de "Nueva cita" desde la ficha del paciente:
  //   ?patientId=       paciente elegido (acción rápida "Agendar")
  //   &procedureId=     procedimiento de la sesión anterior
  //   &fecha=           próxima sesión sugerida (YYYY-MM-DD)
  //   ?appointmentId=   abre el detalle de una cita (enlace "Ver la cita" de Evolución)
  validateSearch: z.object({
    patientId: z.string().optional(),
    procedureId: z.string().optional(),
    fecha: z.string().optional(),
    appointmentId: z.string().optional(),
  }),
  component: CalendarPage,
});

const proceduresRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/procedures",
  component: ProceduresPage,
});

// Paquetes: PackagesController es policy Admin (Esteticista recibe 403 incluso en GET)
const packagesRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/packages",
  beforeLoad: () => requireRoles(["SuperAdmin", "Admin"]),
  component: PackagesPage,
});

// /packages/new ANTES de /packages/$id para que TSR no lo capture como id="new"
const packageNewRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/packages/new",
  beforeLoad: () => requireRoles(["SuperAdmin", "Admin"]),
  component: PackageFormPage,
});

// Importar desde Excel/CSV (ImportsController: policy Admin); /packages/import también ANTES de /packages/$id
const packageImportRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/packages/import",
  beforeLoad: () => requireRoles(["SuperAdmin", "Admin"]),
  component: PackageImportPage,
});

const procedureImportRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/procedures/import",
  beforeLoad: () => requireRoles(["SuperAdmin", "Admin"]),
  component: ProcedureImportPage,
});

const productImportRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/inventory/import",
  beforeLoad: () => requireRoles(["SuperAdmin", "Admin"]),
  component: ProductImportPage,
});

const packageDetailRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/packages/$id",
  beforeLoad: () => requireRoles(["SuperAdmin", "Admin"]),
  component: PackageDetailPage,
});

const packageEditRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/packages/$id/edit",
  beforeLoad: () => requireRoles(["SuperAdmin", "Admin"]),
  component: PackageFormPage,
});

const valuationsRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/valoraciones",
  component: ValuationsPage,
});

const inventoryRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/inventory",
  component: InventoryPage,
});

// ─── Ventas y caja (SalesController / CashController: Admin) ──
// /sales/new ANTES de /sales para que el detalle (?saleId=) no lo capture
const saleNewRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/sales/new",
  beforeLoad: () => requireRoles(["SuperAdmin", "Admin"]),
  //   ?patientId=  venta para un paciente (botón de la ficha)
  //   ?draftId=    retoma un borrador
  validateSearch: z.object({ patientId: z.string().optional(), draftId: z.string().optional() }),
  component: NewSalePage,
});

const salesRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/sales",
  beforeLoad: () => requireRoles(["SuperAdmin", "Admin"]),
  validateSearch: z.object({ saleId: z.string().optional() }),
  component: SalesPage,
});

// Facturación electrónica (InvoicesController: Admin)
const invoicesRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/invoices",
  beforeLoad: () => requireRoles(["SuperAdmin", "Admin"]),
  component: InvoicesPage,
});

const cashRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/cash",
  beforeLoad: () => requireRoles(["SuperAdmin", "Admin"]),
  component: CashPage,
});

// ─── Administración (guard por rol) ────────────────────────
const usersRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/admin/users",
  beforeLoad: () => requireRoles(["SuperAdmin", "Admin"]),
  component: UsersPage,
});

// Configuración en un solo lugar: datos, sedes y horarios, políticas e impuestos.
// El dueño ve todas las pestañas; recepción solo "Sedes y horarios".
const settingsRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/settings",
  beforeLoad: () => requireRoles(["SuperAdmin", "Admin"]),
  validateSearch: z.object({ tab: z.string().optional() }),
  component: SettingsPage,
});

// Rutas anteriores ("Mi clínica" y "Sedes"): llevan a Configuración, por si alguien las tenía guardadas
const branchesRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/admin/branches",
  beforeLoad: () => {
    throw redirect({ to: "/settings", search: { tab: "sedes" } });
  },
});

const tenantsRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/super/tenants",
  beforeLoad: () => {
    throw redirect({ to: "/settings", search: { tab: "datos" } });
  },
});

// Reportes gerenciales (ReportsController: Admin)
const reportsRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/reports",
  beforeLoad: () => requireRoles(["SuperAdmin", "Admin"]),
  validateSearch: z.object({ tab: z.string().optional() }),
  component: ReportsPage,
});

// Cualquier rol (incluido PlatformAdmin). Obligatoria con clave temporal, voluntaria desde el menú de usuario.
const changePasswordRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/change-password",
  component: ChangePasswordPage,
});

// ─── Plataforma (sobre los tenants) ────────────────────────
const platformRoute = createRoute({
  getParentRoute: () => protectedRoute,
  path: "/platform",
  beforeLoad: () => requireRoles([PLATFORM_ADMIN]),
  component: PlatformPage,
});

// ─── Árbol ─────────────────────────────────────────────────
const routeTree = rootRoute.addChildren([
  indexRoute,
  loginRoute,
  protectedRoute.addChildren([
    dashboardRoute,
    patientsRoute,
    patientNewRoute,
    patientImportRoute,
    importsRoute,
    patientDetailRoute,
    patientEditRoute,
    daySheetRoute,
    calendarRoute,
    proceduresRoute,
    packagesRoute,
    packageNewRoute,
    packageImportRoute,
    procedureImportRoute,
    productImportRoute,
    packageDetailRoute,
    packageEditRoute,
    inventoryRoute,
    valuationsRoute,
    saleNewRoute,
    salesRoute,
    invoicesRoute,
    cashRoute,
    usersRoute,
    branchesRoute,
    tenantsRoute,
    settingsRoute,
    reportsRoute,
    platformRoute,
    changePasswordRoute,
  ]),
]);

export const router = createRouter({ routeTree });

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}
