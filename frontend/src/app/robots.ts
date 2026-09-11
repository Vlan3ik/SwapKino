import type { MetadataRoute } from "next";
import { absoluteUrl } from "@/lib/site";

export default function robots(): MetadataRoute.Robots {
  return {
    rules: [
      { userAgent: "*", allow: "/", disallow: ["/api/", "/admin/", "/profile/settings", "/statsadmin/"] },
      { userAgent: ["Googlebot", "Bingbot", "facebookexternalhit", "Twitterbot"], allow: "/", disallow: ["/api/", "/admin/", "/profile/settings", "/statsadmin/"] },
    ],
    sitemap: absoluteUrl("/sitemap.xml"),
    host: absoluteUrl("/"),
  };
}
