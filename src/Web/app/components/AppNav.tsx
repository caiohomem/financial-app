"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

const links = [
  { href: "/", label: "Dashboard" },
  { href: "/import", label: "Importar" },
  { href: "/report", label: "Relatório" },
  { href: "/review", label: "Revisão" },
  { href: "/rules", label: "Regras" },
  { href: "/debug", label: "Debug" },
] as const;

export function AppNav() {
  const pathname = usePathname();

  return (
    <nav
      className="sticky top-0 z-50 h-[72px] border-b"
      style={{
        borderColor: "var(--border)",
        backgroundColor: "rgba(11, 15, 20, 0.8)",
        backdropFilter: "blur(10px)",
        background: "linear-gradient(180deg, rgba(11, 15, 20, 0.95) 0%, rgba(17, 24, 39, 0.9) 100%)",
      }}
      aria-label="Navegação principal"
    >
      <div
        className="h-full flex items-center justify-between px-6 max-w-full mx-auto gap-8"
        style={{ maxWidth: "100%" }}
      >
        {/* Logo */}
        <Link
          href="/"
          className="flex items-center gap-2 font-semibold text-lg tracking-tight transition-opacity hover:opacity-80"
          style={{ color: "var(--text-primary)" }}
        >
          <div
            className="w-8 h-8 rounded-lg flex items-center justify-center font-bold text-sm"
            style={{
              background: "linear-gradient(135deg, var(--accent) 0%, var(--accent-secondary) 100%)",
            }}
          >
            $
          </div>
          Financial
        </Link>

        {/* Menu centralizado */}
        <div className="flex items-center gap-2 flex-wrap justify-center">
          {links.map((link) => {
            const isActive = link.href === "/" ? pathname === "/" : pathname.startsWith(link.href);
            return (
              <Link
                key={link.href}
                href={link.href}
                className="px-3 py-1.5 rounded-lg text-xs font-medium whitespace-nowrap transition-all duration-200"
                style={{
                  color: isActive ? "var(--accent)" : "var(--text-secondary)",
                  backgroundColor: isActive ? "rgba(59, 130, 246, 0.1)" : "transparent",
                  border: isActive ? "1px solid var(--accent)" : "1px solid transparent",
                }}
                onMouseEnter={(e) => {
                  if (!isActive) {
                    e.currentTarget.style.backgroundColor = "rgba(59, 130, 246, 0.05)";
                    e.currentTarget.style.borderColor = "rgba(59, 130, 246, 0.3)";
                  }
                }}
                onMouseLeave={(e) => {
                  if (!isActive) {
                    e.currentTarget.style.backgroundColor = "transparent";
                    e.currentTarget.style.borderColor = "transparent";
                  }
                }}
                aria-current={isActive ? "page" : undefined}
              >
                {link.label}
              </Link>
            );
          })}
        </div>

      </div>
    </nav>
  );
}
