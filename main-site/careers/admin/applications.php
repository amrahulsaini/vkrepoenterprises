<?php
require_once __DIR__ . '/bootstrap-admin.php';
requireLogin();

$search = trim($_GET['search'] ?? '');
$page = max(1, (int) ($_GET['page'] ?? 1));

$db = getDbInstance();
if ($search !== '') {
    $db->where('full_name', '%' . $search . '%', 'like');
    $db->orWhere('email', '%' . $search . '%', 'like');
    $db->orWhere('job_title', '%' . $search . '%', 'like');
}
$db->orderBy('created_at', 'DESC');
$db->pageLimit = 20;
$apps = $db->arraybuilder()->paginate(TBL_APPLICATIONS, $page);
$totalPages = $db->totalPages;

$active = 'applications';
$heading = 'Applications';
require __DIR__ . '/layout-top.php';
?>
<div class="row-head">
  <form class="filters" method="get" action="applications.php">
    <input type="text" name="search" placeholder="Search name, email or role" value="<?php echo e($search); ?>" style="min-width:280px">
    <button type="submit" class="btn btn-ghost">Search</button>
    <?php if ($search !== '') { ?><a class="btn btn-ghost" href="applications.php">Clear</a><?php } ?>
  </form>
</div>

<div class="table-wrap">
  <table>
    <thead>
      <tr><th>Applicant</th><th>Role</th><th>Contact</th><th>Resume</th><th>Received</th><th>Actions</th></tr>
    </thead>
    <tbody>
      <?php if (empty($apps)) { ?>
        <tr><td colspan="6"><div class="empty">No applications found.</div></td></tr>
      <?php } else { foreach ($apps as $a) { ?>
        <tr>
          <td>
            <b style="color:var(--ink)"><?php echo e($a['full_name']); ?></b>
            <?php if (!empty($a['cover_letter'])) { ?>
              <div class="muted" style="margin-top:4px;max-width:280px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis" title="<?php echo e($a['cover_letter']); ?>"><?php echo e($a['cover_letter']); ?></div>
            <?php } ?>
          </td>
          <td><?php echo e($a['job_title']); ?></td>
          <td>
            <div><a href="mailto:<?php echo e($a['email']); ?>"><?php echo e($a['email']); ?></a></div>
            <div class="muted"><?php echo e($a['phone']); ?></div>
          </td>
          <td>
            <?php if (!empty($a['resume_path'])) { ?>
              <a class="btn btn-ghost btn-sm" href="/<?php echo e($a['resume_path']); ?>" target="_blank" rel="noopener">Download</a>
            <?php } else { ?>
              <span class="muted">None</span>
            <?php } ?>
          </td>
          <td><?php echo formatDate($a['created_at'], 'd M Y, H:i'); ?></td>
          <td>
            <?php if (isSuper()) { ?>
              <form method="post" action="application-delete.php" onsubmit="return confirm('Delete this application?');">
                <input type="hidden" name="id" value="<?php echo (int) $a['id']; ?>">
                <button type="submit" class="btn btn-danger btn-sm">Delete</button>
              </form>
            <?php } else { ?>
              <span class="muted">-</span>
            <?php } ?>
          </td>
        </tr>
      <?php } } ?>
    </tbody>
  </table>
</div>

<?php echo paginationLinks($page, $totalPages, 'applications.php'); ?>
<?php require __DIR__ . '/layout-bottom.php'; ?>
