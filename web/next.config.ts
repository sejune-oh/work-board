import type { NextConfig } from "next";
import { PHASE_PRODUCTION_BUILD } from "next/constants";

const nextConfig = (phase: string): NextConfig => {
  const signInOff = !(process.env.NEXT_PUBLIC_SUPABASE_URL && process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY);
  // Build workers load this file again and inherit the flag, so the warning prints once.
  if (phase === PHASE_PRODUCTION_BUILD && signInOff && !process.env.WORKBOARD_SIGNIN_WARNED) {
    process.env.WORKBOARD_SIGNIN_WARNED = "1";
    console.warn(
      "\n⚠ NEXT_PUBLIC_SUPABASE_URL / NEXT_PUBLIC_SUPABASE_ANON_KEY are not set.\n" +
        "  This build has no GitHub sign-in: /login shows the dev user picker and the API must accept X-Dev-User.\n" +
        "  Set both variables before building for any shared or deployed environment.\n",
    );
  }
  return {
    output: "export",
    images: { unoptimized: true },
  };
};

export default nextConfig;
