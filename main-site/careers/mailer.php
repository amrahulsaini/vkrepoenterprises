<?php

function smtpRead($fp)
{
    $data = '';
    while ($line = fgets($fp, 515)) {
        $data .= $line;
        if (isset($line[3]) && $line[3] === ' ') {
            break;
        }
    }
    return $data;
}

function smtpCmd($fp, $cmd, $expect, &$error)
{
    if ($cmd !== null) {
        fwrite($fp, $cmd . "\r\n");
    }
    $resp = smtpRead($fp);
    $code = (int) substr($resp, 0, 3);
    if (!in_array($code, (array) $expect, true)) {
        $error = 'SMTP ' . trim($resp) . ' (after ' . trim((string) $cmd) . ')';
        return false;
    }
    return true;
}

function sendMail($toEmail, $toName, $subject, $htmlBody, &$error = null)
{
    if (SMTP_HOST === '' || SMTP_USER === '') {
        $error = 'SMTP not configured';
        return false;
    }

    $host = SMTP_HOST;
    $port = SMTP_PORT;
    $fp = @stream_socket_client(
        'tcp://' . $host . ':' . $port,
        $errno,
        $errstr,
        20,
        STREAM_CLIENT_CONNECT
    );
    if (!$fp) {
        $error = 'Connect failed: ' . $errstr;
        return false;
    }
    stream_set_timeout($fp, 20);

    $ehlo = 'EHLO crmrecoverysoftware.com';
    if (!smtpCmd($fp, null, 220, $error)) { fclose($fp); return false; }
    if (!smtpCmd($fp, $ehlo, 250, $error)) { fclose($fp); return false; }

    if ($port == 587) {
        if (!smtpCmd($fp, 'STARTTLS', 220, $error)) { fclose($fp); return false; }
        if (!stream_socket_enable_crypto($fp, true, STREAM_CRYPTO_METHOD_TLS_CLIENT | STREAM_CRYPTO_METHOD_TLSv1_2_CLIENT)) {
            $error = 'TLS negotiation failed';
            fclose($fp);
            return false;
        }
        if (!smtpCmd($fp, $ehlo, 250, $error)) { fclose($fp); return false; }
    }

    if (!smtpCmd($fp, 'AUTH LOGIN', 334, $error)) { fclose($fp); return false; }
    if (!smtpCmd($fp, base64_encode(SMTP_USER), 334, $error)) { fclose($fp); return false; }
    if (!smtpCmd($fp, base64_encode(SMTP_PASS), 235, $error)) { fclose($fp); return false; }

    if (!smtpCmd($fp, 'MAIL FROM:<' . SMTP_FROM . '>', 250, $error)) { fclose($fp); return false; }
    if (!smtpCmd($fp, 'RCPT TO:<' . $toEmail . '>', [250, 251], $error)) { fclose($fp); return false; }
    if (!smtpCmd($fp, 'DATA', 354, $error)) { fclose($fp); return false; }

    $fromHeader = smtpEncodeName(SMTP_FROM_NAME) . ' <' . SMTP_FROM . '>';
    $toHeader = $toName !== '' ? smtpEncodeName($toName) . ' <' . $toEmail . '>' : $toEmail;
    $boundaryless = '';
    $headers = [];
    $headers[] = 'Date: ' . date('r');
    $headers[] = 'From: ' . $fromHeader;
    $headers[] = 'To: ' . $toHeader;
    $headers[] = 'Reply-To: ' . SMTP_FROM;
    $headers[] = 'Subject: ' . smtpEncodeName($subject);
    $headers[] = 'MIME-Version: 1.0';
    $headers[] = 'Content-Type: text/html; charset=UTF-8';
    $headers[] = 'Content-Transfer-Encoding: base64';

    $body = rtrim(chunk_split(base64_encode($htmlBody)));
    $message = implode("\r\n", $headers) . "\r\n\r\n" . $body . "\r\n.";

    if (!smtpCmd($fp, $message, 250, $error)) { fclose($fp); return false; }
    smtpCmd($fp, 'QUIT', [221, 250], $error);
    fclose($fp);

    return true;
}

function smtpEncodeName($text)
{
    if (preg_match('/[^\x20-\x7e]/', $text)) {
        return '=?UTF-8?B?' . base64_encode($text) . '?=';
    }
    return $text;
}

function emailLayout($heading, $bodyHtml)
{
    $safeHeading = htmlspecialchars($heading, ENT_QUOTES, 'UTF-8');
    return '<!DOCTYPE html><html><head><meta charset="utf-8"></head>'
        . '<body style="margin:0;padding:0;background:#f4f2e9;font-family:Arial,Helvetica,sans-serif;color:#3a3833;">'
        . '<div style="max-width:560px;margin:0 auto;padding:32px 20px;">'
        . '<div style="background:#100f0c;border-radius:16px 16px 0 0;padding:26px 30px;">'
        . '<div style="font-size:22px;font-weight:800;color:#ffffff;letter-spacing:-0.5px;">CRMRS<span style="color:#ff5500;">.</span> '
        . '<span style="font-size:11px;font-weight:600;color:rgba(255,255,255,0.55);letter-spacing:2px;">RECOVERY SOFTWARE</span></div>'
        . '</div>'
        . '<div style="background:#ffffff;border:1px solid #e7e4da;border-top:0;border-radius:0 0 16px 16px;padding:32px 30px;">'
        . '<h1 style="margin:0 0 18px;font-size:22px;color:#100f0c;">' . $safeHeading . '</h1>'
        . $bodyHtml
        . '</div>'
        . '<div style="text-align:center;padding:22px 10px;font-size:12px;color:#8a877e;">'
        . '&copy; ' . date('Y') . ' CRMRS — Recovery Software &middot; crmrecoverysoftware.com</div>'
        . '</div></body></html>';
}

function sendApplicationEmails($app)
{
    $name = htmlspecialchars($app['full_name'], ENT_QUOTES, 'UTF-8');
    $role = htmlspecialchars($app['job_title'], ENT_QUOTES, 'UTF-8');

    $applicantBody = '<p style="font-size:15px;line-height:1.7;">Hi ' . $name . ',</p>'
        . '<p style="font-size:15px;line-height:1.7;">Thank you for applying for the role of <strong>' . $role . '</strong> at CRMRS. '
        . 'We have received your application and our team will review it shortly. If your profile matches what we are looking for, we will reach out to you directly.</p>'
        . '<p style="font-size:15px;line-height:1.7;">We appreciate your interest in joining us.</p>'
        . '<p style="font-size:15px;line-height:1.7;margin-top:24px;">Warm regards,<br><strong>The CRMRS Team</strong></p>';

    sendMail($app['email'], $app['full_name'], 'We received your application — ' . $app['job_title'], emailLayout('Application received', $applicantBody), $e1);

    $teamBody = '<p style="font-size:15px;line-height:1.7;">A new application has been submitted.</p>'
        . '<table style="width:100%;border-collapse:collapse;font-size:14px;">'
        . emailRow('Role', $role)
        . emailRow('Name', $name)
        . emailRow('Email', htmlspecialchars($app['email'], ENT_QUOTES, 'UTF-8'))
        . emailRow('Phone', htmlspecialchars($app['phone'], ENT_QUOTES, 'UTF-8'))
        . '</table>'
        . '<p style="font-size:14px;line-height:1.7;margin-top:20px;">Review it in the admin panel: '
        . '<a href="https://crmrecoverysoftware.com/careers/admin/applications.php" style="color:#ff5500;">Applications</a>.</p>';

    sendMail(CAREERS_TEAM_INBOX, 'CRMRS Careers', 'New application: ' . $app['job_title'] . ' — ' . $app['full_name'], emailLayout('New job application', $teamBody), $e2);
}

function emailRow($label, $value)
{
    return '<tr>'
        . '<td style="padding:8px 12px 8px 0;color:#8a877e;white-space:nowrap;vertical-align:top;">' . $label . '</td>'
        . '<td style="padding:8px 0;color:#100f0c;font-weight:600;">' . $value . '</td>'
        . '</tr>';
}
