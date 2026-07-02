<?php
$pageTitle = isset($pageTitle) ? $pageTitle : 'Careers — CRMRS';
$pageDesc = isset($pageDesc) ? $pageDesc : 'Careers at CRMRS — join the team building loan-recovery software.';
$assetBase = isset($assetBase) ? $assetBase : '';
?>
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="description" content="<?php echo e($pageDesc); ?>">
<title><?php echo e($pageTitle); ?></title>
<link rel="icon" href="<?php echo $assetBase; ?>assets/favicon.ico">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Archivo:wght@500;600;700;800;900&family=Hanken+Grotesk:wght@400;500;600;700&family=JetBrains+Mono:wght@500;600&display=swap" rel="stylesheet">
<style>
  :root{
    --bg:#fbfaf7;--bg-2:#f4f2e9;--ink:#100f0c;--ink-soft:rgba(16,15,12,0.75);
    --muted:rgba(16,15,12,0.55);--line:rgba(16,15,12,0.08);
    --orange:#ff5500;--maxw:1180px;--card:#ffffff;
  }
  @keyframes shine{to{background-position:200% center}}
  *{margin:0;padding:0;box-sizing:border-box}
  html{scroll-behavior:smooth}
  body{
    font-family:'Hanken Grotesk',-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif;
    line-height:1.6;color:var(--ink-soft);background:var(--bg);
    -webkit-font-smoothing:antialiased;display:flex;flex-direction:column;min-height:100vh;
  }
  .wrap{width:100%;max-width:var(--maxw);margin:0 auto;padding:0 24px}
  a{color:inherit;text-decoration:none}
  h1,h2,h3{font-family:'Archivo',sans-serif;font-weight:800;letter-spacing:-.02em;color:var(--ink)}
  .mono{font-family:'JetBrains Mono',monospace}
  nav{border-bottom:1px solid var(--line);background:rgba(251,250,247,0.85);backdrop-filter:blur(12px);position:sticky;top:0;z-index:10}
  .nav-inner{display:flex;align-items:center;justify-content:space-between;height:68px}
  .brand{display:flex;align-items:center;gap:12px}
  .brand img{height:34px;width:auto;display:block}
  .brand-text{font-family:'Archivo';font-weight:900;font-size:20px;letter-spacing:-0.01em;color:var(--ink);line-height:1}
  .brand-text small{display:block;font-family:'JetBrains Mono';font-weight:600;font-size:8px;letter-spacing:.28em;color:var(--muted);margin-top:4px}
  .nav-back{font-size:14px;font-weight:600;color:var(--ink-soft);display:inline-flex;align-items:center;gap:6px}
  .nav-back:hover{color:var(--orange)}
  main{flex:1}
  .kicker{font-family:'JetBrains Mono';font-size:11px;font-weight:600;letter-spacing:.24em;text-transform:uppercase;color:var(--muted)}
  footer{background:var(--bg-2);color:var(--ink-soft);padding:80px 0 40px;border-top:1px solid var(--line);margin-top:80px}
  .foot{display:grid;grid-template-columns:2fr 1fr 1fr 1.1fr;gap:40px}
  .foot-brand{display:flex;align-items:center;gap:12px;margin-bottom:18px}
  .foot-brand img{height:36px;width:auto;border-radius:8px;transition:transform .3s ease}
  .foot-brand:hover img{transform:scale(1.05)}
  .foot-brand .brand-text{background:linear-gradient(120deg,var(--ink) 30%,var(--orange) 50%,var(--ink) 70%);background-size:200% auto;-webkit-background-clip:text;background-clip:text;-webkit-text-fill-color:transparent;animation:shine 6s linear infinite}
  .foot p{font-size:14px;line-height:1.7;max-width:320px;color:var(--muted)}
  .fcol h4{font-family:'JetBrains Mono';color:var(--ink);font-size:11px;font-weight:600;letter-spacing:.16em;text-transform:uppercase;margin-bottom:18px}
  .fcol a{display:block;color:var(--ink-soft);font-size:14px;padding:8px 0;transition:color .25s,transform .25s}
  .fcol a:hover{color:var(--orange);transform:translateX(4px)}
  .foot-bottom{margin-top:60px;padding-top:28px;border-top:1px solid var(--line);display:flex;justify-content:space-between;flex-wrap:wrap;gap:12px;font-size:13px;color:var(--muted)}
  @media (max-width:860px){.foot{grid-template-columns:1fr 1fr;gap:34px}}
  @media (max-width:520px){.foot{grid-template-columns:1fr}}
  .top{
    position:fixed;right:28px;bottom:28px;width:48px;height:48px;border-radius:12px;
    background:rgba(255,85,0,0.1);color:var(--ink);border:1px solid rgba(255,85,0,0.3);
    display:flex;align-items:center;justify-content:center;font-size:20px;font-weight:800;
    cursor:pointer;opacity:0;pointer-events:none;transform:translateY(16px);
    transition:all .3s cubic-bezier(0.16,1,0.3,1);z-index:200;backdrop-filter:blur(8px);text-decoration:none;
  }
  .top:hover{background:var(--orange);color:#fff;border-color:transparent;box-shadow:0 8px 25px rgba(255,85,0,0.3);transform:translateY(-3px)}
  .top.show{opacity:1;pointer-events:auto;transform:none}
  .btn{
    display:inline-flex;align-items:center;justify-content:center;gap:8px;
    font-weight:700;font-size:15px;padding:13px 22px;border-radius:12px;
    background:var(--ink);color:#fff;transition:transform .2s,background .2s;border:0;cursor:pointer;
  }
  .btn:hover{transform:translateY(-2px);background:#000}
  .btn-orange{background:var(--orange)}
  .btn-orange:hover{background:#e64d00}
  .btn-ghost{background:transparent;color:var(--ink);border:1px solid var(--line)}
  .btn-ghost:hover{background:#fff;border-color:rgba(16,15,12,0.18)}
</style>
</head>
<body>
<nav>
  <div class="wrap nav-inner">
    <a class="brand" href="/" aria-label="CRMRS home">
      <img src="<?php echo $assetBase; ?>assets/logo.webp" alt="CRMRS logo">
      <span class="brand-text">CRMRS<small>RECOVERY SOFTWARE</small></span>
    </a>
    <a class="nav-back" href="/">Back to home</a>
  </div>
</nav>
<span id="top-anchor"></span>
<main>
