<?php

$cfg = [
    'host' => getenv('CAREERS_DB_HOST') ?: '127.0.0.1',
    'user' => getenv('CAREERS_DB_USER') ?: 'root',
    'pass' => getenv('CAREERS_DB_PASSWORD') !== false ? getenv('CAREERS_DB_PASSWORD') : '',
    'name' => getenv('CAREERS_DB_NAME') ?: 'crm_master',
    'port' => getenv('CAREERS_DB_PORT') ?: 3306,
];

$override = __DIR__ . '/config.local.php';
if (file_exists($override)) {
    $cfg = array_merge($cfg, require $override);
}

define('CAREERS_DB_HOST', $cfg['host']);
define('CAREERS_DB_USER', $cfg['user']);
define('CAREERS_DB_PASSWORD', $cfg['pass']);
define('CAREERS_DB_NAME', $cfg['name']);
define('CAREERS_DB_PORT', (int) $cfg['port']);

define('TBL_JOBS', 'careers_jobs');
define('TBL_APPLICATIONS', 'careers_applications');
define('TBL_ADMINS', 'careers_admins');

define('RESUME_DIR', __DIR__ . '/uploads/resumes');
define('RESUME_URL', 'careers/uploads/resumes');
define('RESUME_MAX_BYTES', 5 * 1024 * 1024);
