<?php
require_once __DIR__ . '/careers/bootstrap.php';

$db = getDbInstance();
$db->where('status', 'Open');
$db->orderBy('created_at', 'DESC');
$jobs = $db->get(TBL_JOBS);

$pageTitle = 'Careers — CRMRS';
$pageDesc = 'Open roles at CRMRS — join the team building loan-recovery software for serious agencies.';
$assetBase = '';
require __DIR__ . '/careers/partials/site-head.php';
?>
<style>
  .careers{padding:80px 0 40px;width:100%}
  .careers-head{max-width:640px;margin-bottom:44px}
  .careers h1{font-size:clamp(34px,6vw,54px);line-height:1.05;margin:16px 0 18px}
  .careers h1 .o{color:var(--orange)}
  .lead{font-size:18px;color:var(--ink-soft);margin-bottom:10px}
  .sub{font-size:15px;color:var(--muted)}
  .job-grid{display:grid;grid-template-columns:1fr;gap:16px}
  .job-card{
    display:flex;justify-content:space-between;align-items:center;gap:24px;
    background:var(--card);border:1px solid var(--line);border-radius:16px;
    padding:26px 28px;transition:border-color .2s,transform .2s,box-shadow .2s;
  }
  .job-card:hover{transform:translateY(-3px);border-color:rgba(16,15,12,0.16);box-shadow:0 14px 30px rgba(16,15,12,0.06)}
  .job-card .info{min-width:0}
  .job-card h2{font-size:21px;line-height:1.25;margin-bottom:12px}
  .tags{display:flex;flex-wrap:wrap;gap:8px}
  .tag{
    font-family:'JetBrains Mono';font-size:11px;font-weight:600;letter-spacing:.04em;
    color:var(--ink-soft);background:#f3f1ec;border:1px solid var(--line);
    padding:5px 11px;border-radius:20px;white-space:nowrap;
  }
  .tag.o{color:var(--orange);background:#fff1ea;border-color:rgba(255,85,0,0.18)}
  .job-card .go{flex:none}
  .panel{border:1px solid var(--line);border-radius:16px;background:var(--card);padding:28px 30px}
  .panel-row{display:flex;align-items:center;gap:14px}
  .dot{width:10px;height:10px;border-radius:50%;background:#c9cdd2;flex:none}
  .panel h2{font-size:18px;margin-bottom:2px}
  .panel p{font-size:14px;color:var(--muted)}
  .after{margin-top:40px;max-width:640px}
  .after .sub{margin-bottom:18px}
  @media(max-width:640px){
    .careers{padding:56px 0 30px}
    .job-card{flex-direction:column;align-items:flex-start;gap:18px}
    .job-card .go{width:100%}
    .job-card .go .btn{width:100%}
  }
</style>

<section class="careers">
  <div class="wrap">
    <div class="careers-head">
      <span class="kicker">Careers at CRMRS</span>
      <h1>Join our <span class="o">team</span>.</h1>
      <p class="lead">We build loan-recovery software used by serious agencies across the country.</p>
      <p class="sub">Explore our current openings below and become part of CRMRS.</p>
    </div>

    <?php if (!empty($jobs)) { ?>
      <div class="job-grid">
        <?php foreach ($jobs as $job) { ?>
          <div class="job-card">
            <div class="info">
              <h2><?php echo e($job['job_title']); ?></h2>
              <div class="tags">
                <span class="tag o"><?php echo e($job['employment_type']); ?></span>
                <span class="tag"><?php echo e($job['department']); ?></span>
                <span class="tag"><?php echo e($job['location']); ?></span>
                <span class="tag"><?php echo e($job['work_mode']); ?></span>
                <span class="tag"><?php echo e($job['experience']); ?></span>
              </div>
            </div>
            <div class="go">
              <a class="btn btn-orange" href="careers/job.php?id=<?php echo (int) $job['id']; ?>">View &amp; apply</a>
            </div>
          </div>
        <?php } ?>
      </div>
    <?php } else { ?>
      <div class="panel">
        <div class="panel-row">
          <span class="dot"></span>
          <div>
            <h2>No open positions</h2>
            <p>We are not hiring at the moment. Check back later — new roles are posted here as they open.</p>
          </div>
        </div>
      </div>
    <?php } ?>

    <div class="after">
      <p class="sub">Think you'd be a great fit anyway? We're always glad to hear from talented people.</p>
      <a class="btn btn-ghost" href="mailto:team@crmrecoverysoftware.com?subject=Career%20Opportunity%20at%20CRMRS">Get in touch</a>
    </div>
  </div>
</section>

<?php require __DIR__ . '/careers/partials/site-foot.php'; ?>
