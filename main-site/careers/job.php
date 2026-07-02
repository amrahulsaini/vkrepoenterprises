<?php
require_once __DIR__ . '/bootstrap.php';

$jobId = isset($_GET['id']) ? (int) $_GET['id'] : 0;

$db = getDbInstance();
$db->where('id', $jobId);
$db->where('status', 'Open');
$job = $db->getOne(TBL_JOBS);

$submitted = false;
$errors = [];
$old = ['full_name' => '', 'email' => '', 'phone' => '', 'cover_letter' => ''];

if ($_SERVER['REQUEST_METHOD'] === 'POST' && $job) {
    $old['full_name'] = trim($_POST['full_name'] ?? '');
    $old['email'] = trim($_POST['email'] ?? '');
    $old['phone'] = trim($_POST['phone'] ?? '');
    $old['cover_letter'] = trim($_POST['cover_letter'] ?? '');

    if ($old['full_name'] === '') {
        $errors[] = 'Please enter your full name.';
    }
    if ($old['email'] === '' || !filter_var($old['email'], FILTER_VALIDATE_EMAIL)) {
        $errors[] = 'Please enter a valid email address.';
    }
    if ($old['phone'] === '') {
        $errors[] = 'Please enter your phone number.';
    }

    $resumeStored = null;
    if (!isset($_FILES['resume']) || $_FILES['resume']['error'] === UPLOAD_ERR_NO_FILE) {
        $errors[] = 'Please attach your resume.';
    } elseif ($_FILES['resume']['error'] !== UPLOAD_ERR_OK) {
        $errors[] = 'Resume upload failed. Please try again.';
    } else {
        $allowed = ['pdf', 'doc', 'docx'];
        $ext = strtolower(pathinfo($_FILES['resume']['name'], PATHINFO_EXTENSION));
        if (!in_array($ext, $allowed, true)) {
            $errors[] = 'Resume must be a PDF, DOC or DOCX file.';
        } elseif ($_FILES['resume']['size'] > RESUME_MAX_BYTES) {
            $errors[] = 'Resume must be smaller than 5 MB.';
        } else {
            if (!is_dir(RESUME_DIR)) {
                mkdir(RESUME_DIR, 0775, true);
            }
            $safeName = uniqid('resume_', true) . '.' . $ext;
            if (move_uploaded_file($_FILES['resume']['tmp_name'], RESUME_DIR . '/' . $safeName)) {
                $resumeStored = RESUME_URL . '/' . $safeName;
            } else {
                $errors[] = 'Could not save your resume. Please try again.';
            }
        }
    }

    if (empty($errors)) {
        $inserted = $db->insert(TBL_APPLICATIONS, [
            'job_id' => $job['id'],
            'job_title' => $job['job_title'],
            'full_name' => $old['full_name'],
            'email' => $old['email'],
            'phone' => $old['phone'],
            'cover_letter' => $old['cover_letter'],
            'resume_path' => $resumeStored,
            'created_at' => date('Y-m-d H:i:s'),
        ]);
        if ($inserted) {
            $submitted = true;
        } else {
            $errors[] = 'Something went wrong while submitting. Please try again.';
        }
    }
}

$pageTitle = ($job ? $job['job_title'] . ' — ' : '') . 'Careers — CRMRS';
$pageDesc = $job ? ('Apply for ' . $job['job_title'] . ' at CRMRS.') : 'Careers at CRMRS.';
$assetBase = '../';
require __DIR__ . '/partials/site-head.php';
?>
<style>
  .jd{padding:56px 0 40px;max-width:820px;margin:0 auto}
  .jd-back{display:inline-flex;align-items:center;gap:6px;font-size:14px;font-weight:600;color:var(--muted);margin-bottom:24px}
  .jd-back:hover{color:var(--orange)}
  .jd-head{border-bottom:1px solid var(--line);padding-bottom:26px;margin-bottom:28px}
  .jd-head h1{font-size:clamp(28px,5vw,42px);line-height:1.1;margin:14px 0 16px}
  .tags{display:flex;flex-wrap:wrap;gap:8px}
  .tag{font-family:'JetBrains Mono';font-size:11px;font-weight:600;color:var(--ink-soft);background:#f3f1ec;border:1px solid var(--line);padding:5px 11px;border-radius:20px}
  .tag.o{color:var(--orange);background:#fff1ea;border-color:rgba(255,85,0,0.18)}
  .facts{display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:14px;margin:24px 0}
  .fact{border:1px solid var(--line);border-radius:12px;background:var(--card);padding:14px 16px}
  .fact .k{font-family:'JetBrains Mono';font-size:10px;letter-spacing:.16em;text-transform:uppercase;color:var(--muted);margin-bottom:5px}
  .fact .v{font-size:15px;color:var(--ink);font-weight:600}
  .block{margin-top:30px}
  .block h3{font-size:18px;margin-bottom:12px}
  .block .body{border:1px solid var(--line);border-radius:12px;background:var(--card);padding:18px 20px;line-height:1.8;color:var(--ink-soft);font-size:15px}
  .apply{margin-top:44px;border:1px solid var(--line);border-radius:18px;background:var(--card);padding:30px}
  .apply h2{font-size:23px;margin-bottom:6px}
  .apply .hint{font-size:14px;color:var(--muted);margin-bottom:22px}
  .field{margin-bottom:18px}
  .field label{display:block;font-weight:600;color:var(--ink);font-size:14px;margin-bottom:7px}
  .field input,.field textarea{
    width:100%;padding:12px 14px;border:1px solid var(--line);border-radius:10px;
    font-family:inherit;font-size:15px;color:var(--ink);background:#fff;transition:border-color .2s,box-shadow .2s;
  }
  .field input:focus,.field textarea:focus{outline:0;border-color:var(--orange);box-shadow:0 0 0 3px rgba(255,85,0,0.12)}
  .field textarea{min-height:120px;resize:vertical}
  .field input[type=file]{padding:10px 14px;background:#faf9f6}
  .alert{border-radius:12px;padding:16px 18px;margin-bottom:22px;font-size:14px}
  .alert.ok{background:#eaf7ee;border:1px solid #bfe4cc;color:#1c6b38}
  .alert.err{background:#fdeceb;border:1px solid #f2c4c0;color:#a12a22}
  .alert ul{margin:8px 0 0;padding-left:18px}
  .notfound{padding:70px 0;max-width:640px;margin:0 auto;text-align:left}
  @media(max-width:640px){.jd{padding:40px 0 20px}.apply{padding:22px}}
</style>

<div class="wrap">
<?php if (!$job) { ?>
  <div class="notfound">
    <span class="kicker">Careers at CRMRS</span>
    <h1 style="font-size:clamp(28px,5vw,40px);margin:14px 0 14px">Position not available</h1>
    <p class="lead" style="font-size:17px;color:var(--ink-soft);margin-bottom:24px">This opening may have been closed or does not exist.</p>
    <a class="btn btn-orange" href="/careers.php">View open positions</a>
  </div>
<?php } else { ?>
  <div class="jd">
    <a class="jd-back" href="/careers.php">Back to all openings</a>

    <?php if ($submitted) { ?>
      <div class="alert ok"><strong>Application received.</strong> Thank you for applying for <?php echo e($job['job_title']); ?>. Our team will review your profile and get back to you.</div>
      <a class="btn btn-ghost" href="/careers.php">Browse more roles</a>
    <?php } else { ?>

    <div class="jd-head">
      <span class="kicker">Careers at CRMRS</span>
      <h1><?php echo e($job['job_title']); ?></h1>
      <div class="tags">
        <span class="tag o"><?php echo e($job['employment_type']); ?></span>
        <span class="tag"><?php echo e($job['work_mode']); ?></span>
        <span class="tag"><?php echo e($job['location']); ?></span>
      </div>
    </div>

    <div class="facts">
      <div class="fact"><div class="k">Department</div><div class="v"><?php echo e($job['department']); ?></div></div>
      <div class="fact"><div class="k">Experience</div><div class="v"><?php echo e($job['experience']); ?></div></div>
      <div class="fact"><div class="k">Qualification</div><div class="v"><?php echo $job['education'] !== '' ? e($job['education']) : 'Not specified'; ?></div></div>
      <div class="fact"><div class="k">Vacancies</div><div class="v"><?php echo (int) $job['vacancies']; ?></div></div>
      <div class="fact"><div class="k">Salary</div><div class="v"><?php echo $job['salary'] !== '' ? e($job['salary']) : 'Negotiable'; ?></div></div>
      <div class="fact"><div class="k">Apply before</div><div class="v"><?php echo formatDate($job['application_deadline']) ?: 'Open until filled'; ?></div></div>
    </div>

    <div class="block">
      <h3>Required skills</h3>
      <div class="body"><?php echo nl2br(e($job['skills'])); ?></div>
    </div>

    <div class="block">
      <h3>Job description</h3>
      <div class="body"><?php echo nl2br(e($job['job_description'])); ?></div>
    </div>

    <div class="apply">
      <h2>Apply for this role</h2>
      <p class="hint">Fill in your details and attach your resume. Fields marked required must be completed.</p>

      <?php if (!empty($errors)) { ?>
        <div class="alert err"><strong>Please fix the following:</strong>
          <ul><?php foreach ($errors as $er) { echo '<li>' . e($er) . '</li>'; } ?></ul>
        </div>
      <?php } ?>

      <form action="job.php?id=<?php echo (int) $job['id']; ?>" method="post" enctype="multipart/form-data">
        <div class="field">
          <label for="full_name">Full name *</label>
          <input type="text" id="full_name" name="full_name" value="<?php echo e($old['full_name']); ?>" required>
        </div>
        <div class="field">
          <label for="email">Email *</label>
          <input type="email" id="email" name="email" value="<?php echo e($old['email']); ?>" required>
        </div>
        <div class="field">
          <label for="phone">Phone *</label>
          <input type="text" id="phone" name="phone" value="<?php echo e($old['phone']); ?>" required>
        </div>
        <div class="field">
          <label for="cover_letter">Cover letter</label>
          <textarea id="cover_letter" name="cover_letter" placeholder="Tell us why you're a good fit (optional)"><?php echo e($old['cover_letter']); ?></textarea>
        </div>
        <div class="field">
          <label for="resume">Resume * (PDF, DOC or DOCX, max 5 MB)</label>
          <input type="file" id="resume" name="resume" accept=".pdf,.doc,.docx" required>
        </div>
        <button type="submit" class="btn btn-orange">Submit application</button>
      </form>
    </div>

    <?php } ?>
  </div>
<?php } ?>
</div>

<?php require __DIR__ . '/partials/site-foot.php'; ?>
