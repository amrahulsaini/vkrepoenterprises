<?php

if (session_status() === PHP_SESSION_NONE) {
    session_start();
}

require_once dirname(__DIR__) . '/bootstrap.php';

function isLoggedIn()
{
    return isset($_SESSION['admin_logged_in']) && $_SESSION['admin_logged_in'] === true;
}

function isSuper()
{
    return isLoggedIn() && ($_SESSION['admin_type'] ?? '') === 'super';
}

function currentAdminName()
{
    return $_SESSION['admin_name'] ?? 'Admin';
}

function requireLogin()
{
    if (!isLoggedIn()) {
        header('Location: login.php');
        exit;
    }
}

function requireSuper()
{
    requireLogin();
    if (!isSuper()) {
        http_response_code(403);
        exit('403 — Super admin access required.');
    }
}

function flash($type, $message)
{
    $_SESSION['flash'][] = ['type' => $type, 'message' => $message];
}

function takeFlash()
{
    $items = $_SESSION['flash'] ?? [];
    unset($_SESSION['flash']);
    return $items;
}
