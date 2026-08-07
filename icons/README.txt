SwapKino — monochrome icon pack

Folders:
- master/       1024×1024 transparent white + black masters
- header/       transparent PNG 24/32/40/48/64/96/128 px + WEBP 64/128
- favicon/      PNG 16/32/48/64 + multi-size ICO
- app-icons/    Apple/PWA icons

Recommended header:
  swapkino-header-white-40.png  — dark header
  swapkino-header-black-40.png  — light header

Recommended favicon:
  favicon-dark.ico   — white icon for dark browser/UI
  favicon-light.ico  — black icon for light browser/UI
  favicon.ico        — same as favicon-dark.ico

HTML examples:

<link rel="icon" href="/favicon/favicon.ico" sizes="any">
<link rel="icon" type="image/png" sizes="32x32" href="/favicon/favicon-white-32x32.png">
<link rel="apple-touch-icon" href="/app-icons/apple-touch-icon.png">

For adaptive light/dark PNG favicons:
<link rel="icon" type="image/png" href="/favicon/favicon-black-32x32.png" media="(prefers-color-scheme: light)">
<link rel="icon" type="image/png" href="/favicon/favicon-white-32x32.png" media="(prefers-color-scheme: dark)">
