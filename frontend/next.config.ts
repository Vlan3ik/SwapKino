import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone",
  async rewrites() {
    return [
      { source: "/catalog", destination: "/" },
      { source: "/favorites", destination: "/" },
      { source: "/ratings", destination: "/" },
      { source: "/profile", destination: "/" },
      { source: "/license", destination: "/" },
      { source: "/privacy", destination: "/" },
      { source: "/terms", destination: "/" },
      { source: "/movie/:id", destination: "/" },
    ];
  },
  /* config options here */
  typescript: {
    ignoreBuildErrors: true,
  },
  reactStrictMode: false,
};

export default nextConfig;
