import "./globals.css";
import Link from "next/link";
import type { Metadata } from "next";
import type { ReactNode } from "react";
import styles from "./layout.module.css";

export const metadata: Metadata = {
  title: "Financial App",
  description: "Local-only personal finance app",
};

type RootLayoutProps = {
  children: ReactNode;
};

export default function RootLayout({ children }: RootLayoutProps) {
  return (
    <html lang="en">
      <body className={styles.body}>
        <div className={styles.navShell}>
          <nav className={styles.navInner} aria-label="Main navigation">
            <Link href="/" className={styles.brand}>
              Financial App
            </Link>

            <div className={styles.navLinks}>
              <Link href="/" className={styles.navLink}>
                Dashboard
              </Link>
              <Link href="/report" className={styles.navLink}>
                Relatorio
              </Link>
              <Link href="/review" className={styles.navLink}>
                Review
              </Link>
            </div>
          </nav>
        </div>
        {children}
      </body>
    </html>
  );
}
