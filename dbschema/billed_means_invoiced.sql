UPDATE `repo_submissions`
   SET `bill_status` = 'pending',
       `billed_at`   = NULL
 WHERE `bill_status` = 'billed'
   AND `invoice_no` IS NULL
   AND `bill_file`  IS NULL;
