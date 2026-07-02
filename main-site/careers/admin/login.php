<?php
require_once __DIR__ . '/bootstrap-admin.php';

if (isLoggedIn()) {
    header('Location: index.php');
    exit;
}

$error = '';

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    $username = trim($_POST['username'] ?? '');
    $password = $_POST['password'] ?? '';
    $remember = !empty($_POST['remember']);

    $db = getDbInstance();
    $db->where('user_name', $username);
    $account = $db->getOne(TBL_ADMINS);

    if ($account && password_verify($password, $account['password'])) {
        session_regenerate_id(true);
        $_SESSION['admin_logged_in'] = true;
        $_SESSION['admin_id'] = $account['id'];
        $_SESSION['admin_name'] = $account['user_name'];
        $_SESSION['admin_type'] = $account['admin_type'];

        if ($remember) {
            $seriesId = randomString(16);
            $token = secureToken(20);
            $expiry = date('Y-m-d H:i:s', strtotime('+30 days'));
            $db2 = getDbInstance();
            $db2->where('id', $account['id']);
            $db2->update(TBL_ADMINS, [
                'series_id' => $seriesId,
                'remember_token' => password_hash($token, PASSWORD_DEFAULT),
                'expires' => $expiry,
            ]);
            setcookie('series_id', $seriesId, strtotime($expiry), '/');
            setcookie('remember_token', $token, strtotime($expiry), '/');
        }

        header('Location: index.php');
        exit;
    }

    $error = 'Invalid username or password.';
}
?>
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex,nofollow">
<title>Sign in — CRMRS Careers Admin</title>
<link rel="icon" href="/assets/favicon.ico">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Archivo:wght@600;700;800;900&family=Hanken+Grotesk:wght@400;500;600;700&family=JetBrains+Mono:wght@500;600&display=swap" rel="stylesheet">
<link rel="stylesheet" href="assets/admin.css">
</head>
<body>
<div class="login-wrap">
  <form class="login-card" method="post" action="login.php">
    <div class="brand">
      <img src="/assets/logo.webp" alt="CRMRS">
      <b>CRMRS</b>
    </div>
    <h1>Careers admin</h1>
    <p class="hint">Sign in to manage job openings and applications.</p>

    <?php if ($error) { ?>
      <div class="flash error"><?php echo e($error); ?></div>
    <?php } ?>

    <div class="field">
      <label for="username">Username</label>
      <input type="text" id="username" name="username" autocomplete="username" required autofocus>
    </div>
    <div class="field">
      <label for="password">Password</label>
      <input type="password" id="password" name="password" autocomplete="current-password" required>
    </div>
    <label class="checkline"><input type="checkbox" name="remember" value="1"> Keep me signed in for 30 days</label>
    <button type="submit" class="btn btn-orange">Sign in</button>
  </form>
</div>
</body>
</html>
