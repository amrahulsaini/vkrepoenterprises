<?php
require_once __DIR__ . '/bootstrap-admin.php';
requireSuper();

$id = (int) ($_GET['id'] ?? 0);
$isEdit = $id > 0;
$db = getDbInstance();

$user = ['user_name' => '', 'admin_type' => 'admin'];

if ($isEdit) {
    $db->where('id', $id);
    $existing = $db->getOne(TBL_ADMINS, ['id', 'user_name', 'admin_type']);
    if (!$existing) {
        flash('failure', 'User not found.');
        header('Location: admins.php');
        exit;
    }
    $user = $existing;
}

$error = '';

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    $username = trim($_POST['user_name'] ?? '');
    $password = $_POST['password'] ?? '';
    $type = ($_POST['admin_type'] ?? 'admin') === 'super' ? 'super' : 'admin';
    $user = ['user_name' => $username, 'admin_type' => $type];

    if ($username === '') {
        $error = 'Username is required.';
    } elseif (!$isEdit && $password === '') {
        $error = 'Password is required for a new user.';
    } else {
        $dupe = getDbInstance();
        $dupe->where('user_name', $username);
        if ($isEdit) {
            $dupe->where('id', $id, '!=');
        }
        if ($dupe->getValue(TBL_ADMINS, 'count(*)') > 0) {
            $error = 'That username is already taken.';
        }
    }

    if ($error === '') {
        $data = ['user_name' => $username, 'admin_type' => $type];
        if ($password !== '') {
            $data['password'] = password_hash($password, PASSWORD_DEFAULT);
        }
        $save = getDbInstance();
        if ($isEdit) {
            $save->where('id', $id);
            $ok = $save->update(TBL_ADMINS, $data);
            if ($ok || $save->count === 0) {
                flash('success', 'User updated.');
                header('Location: admins.php');
                exit;
            }
        } else {
            if ($save->insert(TBL_ADMINS, $data)) {
                flash('success', 'User created.');
                header('Location: admins.php');
                exit;
            }
        }
        $error = 'Could not save user: ' . $save->getLastError();
    }
}

$active = 'admins';
$heading = $isEdit ? 'Edit admin user' : 'Add admin user';
require __DIR__ . '/layout-top.php';
?>
<div class="panel" style="max-width:520px">
  <?php if ($error) { ?><div class="flash error"><?php echo e($error); ?></div><?php } ?>
  <form method="post" action="<?php echo $isEdit ? 'admin-edit.php?id=' . $id : 'admin-edit.php'; ?>">
    <div class="field">
      <label>Username <span class="req">*</span></label>
      <input type="text" name="user_name" value="<?php echo e($user['user_name']); ?>" autocomplete="off" required>
    </div>
    <div class="field">
      <label>Password <?php echo $isEdit ? '' : '<span class="req">*</span>'; ?></label>
      <input type="password" name="password" autocomplete="new-password" <?php echo $isEdit ? '' : 'required'; ?>>
      <?php if ($isEdit) { ?><div class="muted" style="margin-top:6px">Leave blank to keep the current password.</div><?php } ?>
    </div>
    <div class="field">
      <label>Role <span class="req">*</span></label>
      <select name="admin_type" required>
        <option value="admin"<?php echo $user['admin_type'] === 'admin' ? ' selected' : ''; ?>>Admin — manage jobs &amp; applications</option>
        <option value="super"<?php echo $user['admin_type'] === 'super' ? ' selected' : ''; ?>>Super — full access incl. users &amp; deletes</option>
      </select>
    </div>
    <div style="display:flex;gap:10px;margin-top:6px">
      <button type="submit" class="btn btn-orange"><?php echo $isEdit ? 'Save changes' : 'Create user'; ?></button>
      <a class="btn btn-ghost" href="admins.php">Cancel</a>
    </div>
  </form>
</div>
<?php require __DIR__ . '/layout-bottom.php'; ?>
