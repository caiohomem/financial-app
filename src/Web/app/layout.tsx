import "./globals.css";
import type { Metadata } from "next";
import type { ReactNode } from "react";
import { AppNav } from "./components/AppNav";
import styles from "./layout.module.css";

export const metadata: Metadata = {
  title: "Financial App",
  description: "Aplicação local de finanças pessoais",
};

type RootLayoutProps = {
  children: ReactNode;
};

export default function RootLayout({ children }: RootLayoutProps) {
  return (
    <html lang="pt">
      <body className={styles.body}>
        <AppNav />
        {children}
      </body>
    </html>
  );
}
