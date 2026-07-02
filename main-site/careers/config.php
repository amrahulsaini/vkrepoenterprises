<?php

$cfg = [
    'host' => getenv('CAREERS_DB_HOST') ?: '127.0.0.1',
    'user' => getenv('CAREERS_DB_USER') ?: 'root',
    'pass' => getenv('CAREERS_DB_PASSWORD') !== false ? getenv('CAREERS_DB_PASSWORD') : '',
    'name' => getenv('CAREERS_DB_NAME') ?: 'crm_master',
    'port' => getenv('CAREERS_DB_PORT') ?: 3306,
    'smtp_host' => getenv('SMTP_HOST') ?: '',
    'smtp_port' => getenv('SMTP_PORT') ?: 587,
    'smtp_user' => getenv('SMTP_USER') ?: '',
    'smtp_pass' => getenv('SMTP_PASS') !== false ? getenv('SMTP_PASS') : '',
    'smtp_from' => getenv('SMTP_FROM') ?: 'team@crmrecoverysoftware.com',
    'smtp_from_name' => getenv('SMTP_FROM_NAME') ?: 'CRMRS TEAM',
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

define('SMTP_HOST', $cfg['smtp_host']);
define('SMTP_PORT', (int) $cfg['smtp_port']);
define('SMTP_USER', $cfg['smtp_user']);
define('SMTP_PASS', $cfg['smtp_pass']);
define('SMTP_FROM', $cfg['smtp_from']);
define('SMTP_FROM_NAME', $cfg['smtp_from_name']);

define('CAREERS_TEAM_INBOX', getenv('CAREERS_TEAM_INBOX') ?: 'team@crmrecoverysoftware.com');

define('TBL_JOBS', 'careers_jobs');
define('TBL_APPLICATIONS', 'careers_applications');
define('TBL_ADMINS', 'careers_admins');

define('RESUME_DIR', __DIR__ . '/uploads/resumes');
define('RESUME_URL', 'careers/uploads/resumes');
define('RESUME_MAX_BYTES', 5 * 1024 * 1024);
