<?php
require_once __DIR__ . '/bootstrap-admin.php';

if (!empty($_SESSION['admin_id']) && isset($_COOKIE['series_id'])) {
    $db = getDbInstance();
    $db->where('id', $_SESSION['admin_id']);
    $db->update(TBL_ADMINS, ['series_id' => null, 'remember_token' => null, 'expires' => null]);
}

clearAuthCookie();
$_SESSION = [];
session_destroy();

header('Location: login.php');
exit;
