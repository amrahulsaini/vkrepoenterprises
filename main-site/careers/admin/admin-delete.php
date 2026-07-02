<?php
require_once __DIR__ . '/bootstrap-admin.php';
requireSuper();

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    $id = (int) ($_POST['id'] ?? 0);
    if ($id > 0 && $id !== (int) $_SESSION['admin_id']) {
        $db = getDbInstance();
        $db->where('id', $id);
        if ($db->delete(TBL_ADMINS)) {
            flash('info', 'Admin user deleted.');
        } else {
            flash('failure', 'Could not delete user.');
        }
    } else {
        flash('failure', 'You cannot delete your own account.');
    }
}

header('Location: admins.php');
exit;
