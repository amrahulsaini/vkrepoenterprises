<?php
$active = $active ?? '';
$heading = $heading ?? 'Dashboard';
$flashes = takeFlash();
?>
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex,nofollow">
<title><?php echo e($heading); ?> — CRMRS Careers Admin</title>
<link rel="icon" href="/assets/favicon.ico">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Archivo:wght@600;700;800;900&family=Hanken+Grotesk:wght@400;500;600;700&family=JetBrains+Mono:wght@500;600&display=swap" rel="stylesheet">
<link rel="stylesheet" href="assets/admin.css">
</head>
<body>
<div class="shell">
  <aside class="sidebar">
    <a class="brand" href="index.php">
      <img src="/assets/logo.webp" alt="CRMRS">
      <span><b>CRMRS</b><small>CAREERS ADMIN</small></span>
    </a>
    <nav>
      <a href="index.php" class="<?php echo $active === 'dashboard' ? 'on' : ''; ?>">Dashboard</a>
      <div class="sep">Recruitment</div>
      <a href="jobs.php" class="<?php echo $active === 'jobs' ? 'on' : ''; ?>">Job openings</a>
      <a href="applications.php" class="<?php echo $active === 'applications' ? 'on' : ''; ?>">Applications</a>
      <?php if (isSuper()) { ?>
        <div class="sep">Settings</div>
        <a href="admins.php" class="<?php echo $active === 'admins' ? 'on' : ''; ?>">Admin users</a>
      <?php } ?>
    </nav>
    <div class="foot">
      Signed in as <b><?php echo e(currentAdminName()); ?></b><br>
      <a href="logout.php">Sign out</a>
    </div>
  </aside>
  <div class="main">
    <div class="topbar">
      <h1><?php echo e($heading); ?></h1>
      <div class="who">Signed in as <b><?php echo e(currentAdminName()); ?></b>
        <?php echo isSuper() ? '<span class="badge super" style="margin-left:8px">Super</span>' : ''; ?>
      </div>
    </div>
    <div class="content">
      <?php foreach ($flashes as $f) { ?>
        <div class="flash <?php echo e($f['type']); ?>"><?php echo e($f['message']); ?></div>
      <?php } ?>
