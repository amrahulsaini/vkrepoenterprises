<?php

function e($string)
{
    if ($string === null || $string === '') {
        return '';
    }
    return htmlspecialchars($string, ENT_QUOTES, 'UTF-8');
}

function randomString($n)
{
    $domain = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890';
    $len = strlen($domain);
    $out = '';
    for ($i = 0; $i < $n; $i++) {
        $out .= $domain[random_int(0, $len - 1)];
    }
    return $out;
}

function secureToken($bytes = 20)
{
    return bin2hex(random_bytes($bytes));
}

function clearAuthCookie()
{
    setcookie('series_id', '', time() - 3600, '/');
    setcookie('remember_token', '', time() - 3600, '/');
    unset($_COOKIE['series_id'], $_COOKIE['remember_token']);
}

function formatDate($value, $format = 'd M Y')
{
    if (empty($value) || $value === '0000-00-00') {
        return '';
    }
    $ts = strtotime($value);
    return $ts ? date($format, $ts) : '';
}

function paginationLinks($currentPage, $totalPages, $baseUrl)
{
    if ($totalPages <= 1) {
        return '';
    }

    $query = $_GET;
    unset($query['page']);
    $prefix = empty($query) ? '?' : '?' . http_build_query($query) . '&';

    $html = '<ul class="pager">';
    for ($i = 1; $i <= $totalPages; $i++) {
        $active = ($currentPage == $i) ? ' class="on"' : '';
        $html .= '<li' . $active . '><a href="' . $baseUrl . $prefix . 'page=' . $i . '">' . $i . '</a></li>';
    }
    $html .= '</ul>';

    return $html;
}
