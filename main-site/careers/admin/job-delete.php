<?php
require_once __DIR__ . '/bootstrap-admin.php';
requireSuper();

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    $id = (int) ($_POST['id'] ?? 0);
    if ($id > 0) {
        $db = getDbInstance();
        $db->where('id', $id);
        if ($db->delete(TBL_JOBS)) {
            flash('info', 'Job opening deleted.');
        } else {
            flash('failure', 'Could not delete job.');
        }
    }
}

header('Location: jobs.php');
exit;
