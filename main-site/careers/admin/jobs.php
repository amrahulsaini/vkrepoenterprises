<?php
require_once __DIR__ . '/bootstrap-admin.php';
requireLogin();

$search = trim($_GET['search'] ?? '');
$page = max(1, (int) ($_GET['page'] ?? 1));

$db = getDbInstance();
if ($search !== '') {
    $db->where('job_title', '%' . $search . '%', 'like');
}
$db->orderBy('created_at', 'DESC');
$db->pageLimit = 15;
$select = ['id', 'job_title', 'department', 'location', 'employment_type', 'vacancies', 'status'];
$jobs = $db->arraybuilder()->paginate(TBL_JOBS, $page, $select);
$totalPages = $db->totalPages;

$active = 'jobs';
$heading = 'Job openings';
require __DIR__ . '/layout-top.php';
?>
<div class="row-head">
  <form class="filters" method="get" action="jobs.php">
    <input type="text" name="search" placeholder="Search job title" value="<?php echo e($search); ?>" style="min-width:240px">
    <button type="submit" class="btn btn-ghost">Search</button>
    <?php if ($search !== '') { ?><a class="btn btn-ghost" href="jobs.php">Clear</a><?php } ?>
  </form>
  <a class="btn btn-orange" href="job-edit.php">Add job</a>
</div>

<div class="table-wrap">
  <table>
    <thead>
      <tr><th>Title</th><th>Department</th><th>Location</th><th>Type</th><th>Vacancies</th><th>Status</th><th>Actions</th></tr>
    </thead>
    <tbody>
      <?php if (empty($jobs)) { ?>
        <tr><td colspan="7"><div class="empty">No job openings found.</div></td></tr>
      <?php } else { foreach ($jobs as $job) {
          $badge = strtolower(str_replace(' ', '', $job['status']));
      ?>
        <tr>
          <td><b style="color:var(--ink)"><?php echo e($job['job_title']); ?></b></td>
          <td><?php echo e($job['department']); ?></td>
          <td><?php echo e($job['location']); ?></td>
          <td><?php echo e($job['employment_type']); ?></td>
          <td><?php echo (int) $job['vacancies']; ?></td>
          <td><span class="badge <?php echo e($badge); ?>"><?php echo e($job['status']); ?></span></td>
          <td>
            <div class="acts">
              <a class="btn btn-ghost btn-sm" href="job-edit.php?id=<?php echo (int) $job['id']; ?>">Edit</a>
              <?php if (isSuper()) { ?>
                <form method="post" action="job-delete.php" onsubmit="return confirm('Delete this job opening? This cannot be undone.');">
                  <input type="hidden" name="id" value="<?php echo (int) $job['id']; ?>">
                  <button type="submit" class="btn btn-danger btn-sm">Delete</button>
                </form>
              <?php } ?>
            </div>
          </td>
        </tr>
      <?php } } ?>
    </tbody>
  </table>
</div>

<?php echo paginationLinks($page, $totalPages, 'jobs.php'); ?>
<?php require __DIR__ . '/layout-bottom.php'; ?>
