<?php
require_once __DIR__ . '/bootstrap-admin.php';
requireSuper();

$page = max(1, (int) ($_GET['page'] ?? 1));

$db = getDbInstance();
$db->orderBy('id', 'ASC');
$db->pageLimit = 20;
$rows = $db->arraybuilder()->paginate(TBL_ADMINS, $page, ['id', 'user_name', 'admin_type']);
$totalPages = $db->totalPages;

$active = 'admins';
$heading = 'Admin users';
require __DIR__ . '/layout-top.php';
?>
<div class="row-head">
  <p class="muted">Users who can sign in to this panel. Super admins can manage jobs, applications and other users.</p>
  <a class="btn btn-orange" href="admin-edit.php">Add user</a>
</div>

<div class="table-wrap">
  <table>
    <thead><tr><th>ID</th><th>Username</th><th>Role</th><th>Actions</th></tr></thead>
    <tbody>
      <?php foreach ($rows as $row) { ?>
        <tr>
          <td><?php echo (int) $row['id']; ?></td>
          <td><b style="color:var(--ink)"><?php echo e($row['user_name']); ?></b></td>
          <td><span class="badge <?php echo $row['admin_type'] === 'super' ? 'super' : ''; ?>"><?php echo e($row['admin_type']); ?></span></td>
          <td>
            <div class="acts">
              <a class="btn btn-ghost btn-sm" href="admin-edit.php?id=<?php echo (int) $row['id']; ?>">Edit</a>
              <?php if ((int) $row['id'] !== (int) $_SESSION['admin_id']) { ?>
                <form method="post" action="admin-delete.php" onsubmit="return confirm('Delete this admin user?');">
                  <input type="hidden" name="id" value="<?php echo (int) $row['id']; ?>">
                  <button type="submit" class="btn btn-danger btn-sm">Delete</button>
                </form>
              <?php } ?>
            </div>
          </td>
        </tr>
      <?php } ?>
    </tbody>
  </table>
</div>

<?php echo paginationLinks($page, $totalPages, 'admins.php'); ?>
<?php require __DIR__ . '/layout-bottom.php'; ?>
