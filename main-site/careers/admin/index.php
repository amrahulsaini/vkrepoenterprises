<?php
require_once __DIR__ . '/bootstrap-admin.php';
requireLogin();

$db = getDbInstance();
$totalJobs = (int) $db->getValue(TBL_JOBS, 'count(*)');

$db->where('status', 'Open');
$openJobs = (int) $db->getValue(TBL_JOBS, 'count(*)');

$totalApplications = (int) $db->getValue(TBL_APPLICATIONS, 'count(*)');

$db->orderBy('created_at', 'DESC');
$recent = $db->get(TBL_APPLICATIONS, 5, ['id', 'full_name', 'job_title', 'email', 'created_at']);

$active = 'dashboard';
$heading = 'Dashboard';
require __DIR__ . '/layout-top.php';
?>
<div class="cards">
  <div class="stat">
    <div class="n"><?php echo $openJobs; ?></div>
    <div class="l">Open positions</div>
    <a href="jobs.php">Manage jobs</a>
  </div>
  <div class="stat">
    <div class="n"><?php echo $totalJobs; ?></div>
    <div class="l">Total job records</div>
    <a href="job-edit.php">Add a job</a>
  </div>
  <div class="stat">
    <div class="n"><?php echo $totalApplications; ?></div>
    <div class="l">Applications received</div>
    <a href="applications.php">View applications</a>
  </div>
</div>

<div class="panel">
  <h2>Recent applications</h2>
  <?php if (empty($recent)) { ?>
    <div class="empty">No applications yet.</div>
  <?php } else { ?>
    <div class="table-wrap">
      <table>
        <thead><tr><th>Applicant</th><th>Role</th><th>Email</th><th>Received</th></tr></thead>
        <tbody>
          <?php foreach ($recent as $r) { ?>
            <tr>
              <td><?php echo e($r['full_name']); ?></td>
              <td><?php echo e($r['job_title']); ?></td>
              <td><?php echo e($r['email']); ?></td>
              <td><?php echo formatDate($r['created_at'], 'd M Y, H:i'); ?></td>
            </tr>
          <?php } ?>
        </tbody>
      </table>
    </div>
  <?php } ?>
</div>
<?php require __DIR__ . '/layout-bottom.php'; ?>
