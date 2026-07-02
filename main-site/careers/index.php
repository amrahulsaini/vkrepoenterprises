<?php
require_once __DIR__ . '/bootstrap.php';

$db = getDbInstance();
$db->where('status', 'Open');
$db->orderBy('created_at', 'DESC');
$jobs = $db->get(TBL_JOBS);

$pageTitle = 'Careers — CRMRS';
$pageDesc = 'Open roles at CRMRS — join the team building loan-recovery software for serious agencies.';
$assetBase = '../';
require __DIR__ . '/partials/site-head.php';
?>
<style>
  .careers{padding:88px 0 40px;width:100%}
  .careers-head{max-width:660px;margin-bottom:48px}
  .careers h1{font-size:clamp(36px,6vw,58px);line-height:1.03;margin:16px 0 20px}
  .careers h1 .o{color:var(--orange)}
  .lead{font-size:19px;color:var(--ink-soft);margin-bottom:10px}
  .sub{font-size:15px;color:var(--muted)}
  .count-pill{display:inline-flex;align-items:center;gap:8px;margin-bottom:22px;font-family:'JetBrains Mono';font-size:12px;font-weight:600;color:var(--orange);background:#fff1ea;border:1px solid rgba(255,85,0,0.18);padding:6px 14px;border-radius:30px}
  .count-pill .d{width:7px;height:7px;border-radius:50%;background:var(--orange)}
  .job-grid{display:grid;grid-template-columns:1fr;gap:16px}
  .job-card{
    display:flex;justify-content:space-between;align-items:center;gap:24px;
    background:var(--card);border:1px solid var(--line);border-radius:18px;
    padding:26px 30px;transition:border-color .2s,transform .2s,box-shadow .2s;
    text-decoration:none;
  }
  .job-card:hover{transform:translateY(-3px);border-color:rgba(255,85,0,0.35);box-shadow:0 18px 40px rgba(16,15,12,0.07)}
  .job-card .info{min-width:0}
  .job-card h2{font-size:22px;line-height:1.25;margin-bottom:12px;transition:color .2s}
  .job-card:hover h2{color:var(--orange)}
  .tags{display:flex;flex-wrap:wrap;gap:8px}
  .tag{
    font-family:'JetBrains Mono';font-size:11px;font-weight:600;letter-spacing:.03em;
    color:var(--ink-soft);background:#f3f1ec;border:1px solid var(--line);
    padding:5px 11px;border-radius:20px;white-space:nowrap;
  }
  .tag.o{color:var(--orange);background:#fff1ea;border-color:rgba(255,85,0,0.18)}
  .job-card .go{flex:none;display:inline-flex;align-items:center;gap:8px;font-weight:700;font-size:15px;color:var(--ink)}
  .job-card .go .arrow{display:inline-flex;width:34px;height:34px;border-radius:10px;background:var(--ink);color:#fff;align-items:center;justify-content:center;transition:background .2s,transform .2s}
  .job-card:hover .go .arrow{background:var(--orange);transform:translateX(3px)}
  .panel{border:1px solid var(--line);border-radius:18px;background:var(--card);padding:30px 32px}
  .panel-row{display:flex;align-items:center;gap:14px}
  .dot{width:10px;height:10px;border-radius:50%;background:#c9cdd2;flex:none}
  .panel h2{font-size:18px;margin-bottom:2px}
  .panel p{font-size:14px;color:var(--muted)}
  .after{margin-top:44px;max-width:640px}
  .after .sub{margin-bottom:18px}
  @media(max-width:640px){
    .careers{padding:60px 0 30px}
    .job-card{flex-direction:column;align-items:flex-start;gap:18px;padding:24px}
    .job-card .go{width:100%;justify-content:space-between}
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
      <div class="count-pill"><span class="d"></span><?php echo count($jobs); ?> open <?php echo count($jobs) === 1 ? 'position' : 'positions'; ?></div>
      <div class="job-grid">
        <?php foreach ($jobs as $job) { ?>
          <a class="job-card" href="<?php echo e(jobUrl($job)); ?>">
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
            <span class="go">View &amp; apply <span class="arrow">&#8594;</span></span>
          </a>
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

<?php require __DIR__ . '/partials/site-foot.php'; ?>
