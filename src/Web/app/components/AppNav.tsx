"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import styles from "../layout.module.css";

const links = [
  { href: "/", label: "Dashboard" },
  { href: "/import", label: "Importar" },
  { href: "/report", label: "Relatório" },
  { href: "/review", label: "Revisão" },
] as const;

export function AppNav() {
  const pathname = usePathname();

  return (
    <div className={styles.navShell}>
      <nav className={styles.navInner} aria-label="Navegação principal">
        <Link href="/" className={styles.brand}>
          Financial App
        </Link>

        <div className={styles.navLinks}>
          {links.map((link) => {
            const isActive = link.href === "/" ? pathname === "/" : pathname.startsWith(link.href);

            return (
              <Link
                key={link.href}
                href={link.href}
                className={`${styles.navLink} ${isActive ? styles.navLinkActive : ""}`}
                aria-current={isActive ? "page" : undefined}
              >
                {link.label}
              </Link>
            );
          })}
        </div>
      </nav>
    </div>
  );
}
