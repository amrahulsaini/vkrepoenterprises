<?php

error_reporting(E_ALL & ~E_DEPRECATED & ~E_NOTICE);

require_once __DIR__ . '/config.php';
require_once __DIR__ . '/lib/MysqliDb/MysqliDb.php';
require_once __DIR__ . '/helpers.php';

function getDbInstance()
{
    return new MysqliDb(
        CAREERS_DB_HOST,
        CAREERS_DB_USER,
        CAREERS_DB_PASSWORD,
        CAREERS_DB_NAME,
        CAREERS_DB_PORT
    );
}
