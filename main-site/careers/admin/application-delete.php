<?php
require_once __DIR__ . '/bootstrap-admin.php';
requireSuper();

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    $id = (int) ($_POST['id'] ?? 0);
    if ($id > 0) {
        $db = getDbInstance();
        $db->where('id', $id);
        $app = $db->getOne(TBL_APPLICATIONS, ['resume_path']);

        $db->where('id', $id);
        if ($db->delete(TBL_APPLICATIONS)) {
            if (!empty($app['resume_path'])) {
                $file = RESUME_DIR . '/' . basename($app['resume_path']);
                if (is_file($file)) {
                    @unlink($file);
                }
            }
            flash('info', 'Application deleted.');
        } else {
            flash('failure', 'Could not delete application.');
        }
    }
}

header('Location: applications.php');
exit;
