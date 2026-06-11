import type { NextConfig } from "next";

// Server-side rewrite target. In Docker Compose use the internal service URL (http://api:8080).
const apiBaseUrl =
  process.env.API_URL ?? process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:8080";

const nextConfig: NextConfig = {
  experimental: {
    // Allow long-running API calls (e.g. LLM import that can take 2–3 min for batch processing)
    proxyTimeout: 180_000,
  },
  async rewrites() {
    return [
      {
        source: "/api/:path*",
        destination: `${apiBaseUrl}/api/:path*`,
      },
    ];
  },
};

export default nextConfig;
