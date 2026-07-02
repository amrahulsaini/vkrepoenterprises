<?php
require_once __DIR__ . '/bootstrap-admin.php';
requireLogin();

$id = (int) ($_GET['id'] ?? 0);
$isEdit = $id > 0;
$db = getDbInstance();

$job = [
    'job_title' => '', 'department' => '', 'vacancies' => 1, 'employment_type' => '',
    'work_mode' => '', 'location' => '', 'experience' => '', 'education' => '',
    'skills' => '', 'salary' => '', 'application_deadline' => '', 'job_description' => '', 'status' => 'Open',
];

if ($isEdit) {
    $db->where('id', $id);
    $existing = $db->getOne(TBL_JOBS);
    if (!$existing) {
        flash('failure', 'Job not found.');
        header('Location: jobs.php');
        exit;
    }
    $job = array_merge($job, $existing);
}

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    $data = [
        'job_title' => trim($_POST['job_title'] ?? ''),
        'department' => trim($_POST['department'] ?? ''),
        'vacancies' => max(1, (int) ($_POST['vacancies'] ?? 1)),
        'employment_type' => $_POST['employment_type'] ?? '',
        'work_mode' => $_POST['work_mode'] ?? '',
        'location' => trim($_POST['location'] ?? ''),
        'experience' => trim($_POST['experience'] ?? ''),
        'education' => trim($_POST['education'] ?? ''),
        'skills' => trim($_POST['skills'] ?? ''),
        'salary' => trim($_POST['salary'] ?? ''),
        'application_deadline' => $_POST['application_deadline'] ?: null,
        'job_description' => trim($_POST['job_description'] ?? ''),
        'status' => $_POST['status'] ?? 'Open',
    ];

    if ($isEdit) {
        $db->where('id', $id);
        $ok = $db->update(TBL_JOBS, $data);
        if ($ok || $db->count === 0) {
            flash('success', 'Job updated successfully.');
            header('Location: jobs.php');
            exit;
        }
        flash('failure', 'Could not update job: ' . $db->getLastError());
    } else {
        $newId = $db->insert(TBL_JOBS, $data);
        if ($newId) {
            flash('success', 'Job created successfully.');
            header('Location: jobs.php');
            exit;
        }
        flash('failure', 'Could not create job: ' . $db->getLastError());
    }
    $job = array_merge($job, $data);
}

function opt($current, $value)
{
    return $current === $value ? ' selected' : '';
}

$active = 'jobs';
$heading = $isEdit ? 'Edit job opening' : 'Add job opening';
require __DIR__ . '/layout-top.php';
?>
<div class="panel">
  <form method="post" action="<?php echo $isEdit ? 'job-edit.php?id=' . $id : 'job-edit.php'; ?>">
    <div class="grid2">
      <div class="field">
        <label>Job title <span class="req">*</span></label>
        <input type="text" name="job_title" value="<?php echo e($job['job_title']); ?>" placeholder="e.g. ASP.NET Developer" required>
      </div>
      <div class="field">
        <label>Department <span class="req">*</span></label>
        <input type="text" name="department" value="<?php echo e($job['department']); ?>" placeholder="e.g. Engineering" required>
      </div>
      <div class="field">
        <label>Vacancies <span class="req">*</span></label>
        <input type="number" name="vacancies" min="1" value="<?php echo (int) $job['vacancies']; ?>" required>
      </div>
      <div class="field">
        <label>Employment type <span class="req">*</span></label>
        <select name="employment_type" required>
          <option value="">Select type</option>
          <?php foreach (['Full Time', 'Part Time', 'Contract', 'Internship'] as $o) { ?>
            <option value="<?php echo $o; ?>"<?php echo opt($job['employment_type'], $o); ?>><?php echo $o; ?></option>
          <?php } ?>
        </select>
      </div>
      <div class="field">
        <label>Work mode <span class="req">*</span></label>
        <select name="work_mode" required>
          <option value="">Select mode</option>
          <?php foreach (['On-site', 'Hybrid', 'Remote'] as $o) { ?>
            <option value="<?php echo $o; ?>"<?php echo opt($job['work_mode'], $o); ?>><?php echo $o; ?></option>
          <?php } ?>
        </select>
      </div>
      <div class="field">
        <label>Location <span class="req">*</span></label>
        <input type="text" name="location" value="<?php echo e($job['location']); ?>" placeholder="e.g. Satara, Maharashtra" required>
      </div>
      <div class="field">
        <label>Experience <span class="req">*</span></label>
        <input type="text" name="experience" value="<?php echo e($job['experience']); ?>" placeholder="e.g. 2-5 Years / Fresher" required>
      </div>
      <div class="field">
        <label>Qualification</label>
        <input type="text" name="education" value="<?php echo e($job['education']); ?>" placeholder="e.g. B.Tech / MCA">
      </div>
      <div class="field">
        <label>Salary range</label>
        <input type="text" name="salary" value="<?php echo e($job['salary']); ?>" placeholder="e.g. 4,00,000 - 6,00,000">
      </div>
      <div class="field">
        <label>Application deadline</label>
        <input type="date" name="application_deadline" value="<?php echo e($job['application_deadline']); ?>">
      </div>
      <div class="field">
        <label>Status <span class="req">*</span></label>
        <select name="status" required>
          <?php foreach (['Open', 'Draft', 'Closed', 'On Hold'] as $o) { ?>
            <option value="<?php echo $o; ?>"<?php echo opt($job['status'], $o); ?>><?php echo $o; ?></option>
          <?php } ?>
        </select>
      </div>
      <div class="field full">
        <label>Required skills <span class="req">*</span></label>
        <textarea name="skills" placeholder="Comma-separated skills" required><?php echo e($job['skills']); ?></textarea>
      </div>
      <div class="field full">
        <label>Job description <span class="req">*</span></label>
        <textarea name="job_description" style="min-height:160px" placeholder="Describe the role and responsibilities" required><?php echo e($job['job_description']); ?></textarea>
      </div>
    </div>
    <div style="display:flex;gap:10px;margin-top:6px">
      <button type="submit" class="btn btn-orange"><?php echo $isEdit ? 'Save changes' : 'Create job'; ?></button>
      <a class="btn btn-ghost" href="jobs.php">Cancel</a>
    </div>
  </form>
</div>
<?php require __DIR__ . '/layout-bottom.php'; ?>
