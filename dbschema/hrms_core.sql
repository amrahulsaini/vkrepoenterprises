ALTER TABLE `attendance`
  ADD COLUMN IF NOT EXISTS `check_in`       DATETIME NULL AFTER `marked_at`,
  ADD COLUMN IF NOT EXISTS `check_out`      DATETIME NULL AFTER `check_in`,
  ADD COLUMN IF NOT EXISTS `worked_minutes` INT NOT NULL DEFAULT 0 AFTER `check_out`,
  ADD COLUMN IF NOT EXISTS `late_minutes`   INT NOT NULL DEFAULT 0 AFTER `worked_minutes`,
  ADD COLUMN IF NOT EXISTS `early_minutes`  INT NOT NULL DEFAULT 0 AFTER `late_minutes`,
  ADD COLUMN IF NOT EXISTS `leave_type_id`  INT NULL AFTER `early_minutes`;

CREATE TABLE IF NOT EXISTS hrms_settings (
    id                  TINYINT      NOT NULL PRIMARY KEY DEFAULT 1,
    shift_start         TIME         NOT NULL DEFAULT '09:30:00',
    shift_end           TIME         NOT NULL DEFAULT '18:30:00',
    grace_minutes       INT          NOT NULL DEFAULT 15,
    half_day_minutes    INT          NOT NULL DEFAULT 240,
    full_day_minutes    INT          NOT NULL DEFAULT 480,
    weekly_offs         VARCHAR(20)  NOT NULL DEFAULT '0',
    pf_employee_pct     DECIMAL(5,2) NOT NULL DEFAULT 12.00,
    pf_employer_pct     DECIMAL(5,2) NOT NULL DEFAULT 13.00,
    pf_wage_ceiling     INT          NOT NULL DEFAULT 15000,
    pf_limit_to_ceiling TINYINT(1)   NOT NULL DEFAULT 1,
    esic_employee_pct   DECIMAL(5,2) NOT NULL DEFAULT 0.75,
    esic_employer_pct   DECIMAL(5,2) NOT NULL DEFAULT 3.25,
    esic_wage_ceiling   INT          NOT NULL DEFAULT 21000,
    pt_amount           DECIMAL(10,2) NOT NULL DEFAULT 200.00,
    pt_enabled          TINYINT(1)   NOT NULL DEFAULT 1,
    salary_round        TINYINT(1)   NOT NULL DEFAULT 1,
    updated_at          TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

INSERT INTO hrms_settings (id) VALUES (1) ON DUPLICATE KEY UPDATE id = id;

CREATE TABLE IF NOT EXISTS hrms_employment (
    user_id         BIGINT       NOT NULL PRIMARY KEY,
    hired_on        DATE         NULL,
    confirmed_on    DATE         NULL,
    exit_on         DATE         NULL,
    designation     VARCHAR(120) NOT NULL DEFAULT '',
    department      VARCHAR(120) NOT NULL DEFAULT '',
    employment_type ENUM('full_time','part_time','contract','probation') NOT NULL DEFAULT 'full_time',
    reports_to      BIGINT       NULL,
    shift_start     TIME         NULL,
    shift_end       TIME         NULL,
    weekly_offs     VARCHAR(20)  NULL,
    emergency_name  VARCHAR(190) NOT NULL DEFAULT '',
    emergency_phone VARCHAR(20)  NOT NULL DEFAULT '',
    blood_group     VARCHAR(8)   NOT NULL DEFAULT '',
    date_of_birth   DATE         NULL,
    updated_at      TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    CONSTRAINT fk_emp_user FOREIGN KEY (user_id) REFERENCES app_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS hrms_holidays (
    id           INT AUTO_INCREMENT PRIMARY KEY,
    holiday_date DATE         NOT NULL,
    name         VARCHAR(190) NOT NULL,
    is_optional  TINYINT(1)   NOT NULL DEFAULT 0,
    created_at   TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY uq_holiday_date (holiday_date)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS hrms_leave_types (
    id            INT AUTO_INCREMENT PRIMARY KEY,
    code          VARCHAR(12)  NOT NULL,
    name          VARCHAR(120) NOT NULL,
    annual_quota  DECIMAL(5,1) NOT NULL DEFAULT 0,
    is_paid       TINYINT(1)   NOT NULL DEFAULT 1,
    carry_forward TINYINT(1)   NOT NULL DEFAULT 0,
    active        TINYINT(1)   NOT NULL DEFAULT 1,
    UNIQUE KEY uq_leave_code (code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

INSERT INTO hrms_leave_types (code, name, annual_quota, is_paid, carry_forward)
VALUES ('CL', 'Casual Leave', 12, 1, 0),
       ('SL', 'Sick Leave', 6, 1, 0),
       ('EL', 'Earned Leave', 15, 1, 1),
       ('LWP', 'Leave Without Pay', 0, 0, 0)
ON DUPLICATE KEY UPDATE code = VALUES(code);

CREATE TABLE IF NOT EXISTS hrms_leave_balances (
    id            BIGINT AUTO_INCREMENT PRIMARY KEY,
    user_id       BIGINT       NOT NULL,
    leave_type_id INT          NOT NULL,
    year          SMALLINT     NOT NULL,
    opening       DECIMAL(5,1) NOT NULL DEFAULT 0,
    accrued       DECIMAL(5,1) NOT NULL DEFAULT 0,
    used          DECIMAL(5,1) NOT NULL DEFAULT 0,
    UNIQUE KEY uq_bal (user_id, leave_type_id, year),
    CONSTRAINT fk_bal_user FOREIGN KEY (user_id) REFERENCES app_users(id) ON DELETE CASCADE,
    CONSTRAINT fk_bal_type FOREIGN KEY (leave_type_id) REFERENCES hrms_leave_types(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS hrms_leave_requests (
    id            BIGINT AUTO_INCREMENT PRIMARY KEY,
    user_id       BIGINT       NOT NULL,
    leave_type_id INT          NOT NULL,
    from_date     DATE         NOT NULL,
    to_date       DATE         NOT NULL,
    days          DECIMAL(5,1) NOT NULL,
    half_day      ENUM('none','first','second') NOT NULL DEFAULT 'none',
    reason        VARCHAR(500) NOT NULL DEFAULT '',
    status        ENUM('pending','approved','rejected','cancelled') NOT NULL DEFAULT 'pending',
    applied_at    DATETIME     NOT NULL,
    decided_by    VARCHAR(190) NULL,
    decided_at    DATETIME     NULL,
    decision_note VARCHAR(500) NULL,
    INDEX idx_lr_user (user_id, from_date),
    INDEX idx_lr_status (status, from_date),
    CONSTRAINT fk_lr_user FOREIGN KEY (user_id) REFERENCES app_users(id) ON DELETE CASCADE,
    CONSTRAINT fk_lr_type FOREIGN KEY (leave_type_id) REFERENCES hrms_leave_types(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS hrms_documents (
    id          BIGINT AUTO_INCREMENT PRIMARY KEY,
    user_id     BIGINT       NOT NULL,
    title       VARCHAR(190) NOT NULL,
    kind        VARCHAR(40)  NOT NULL DEFAULT 'other',
    file_path   VARCHAR(400) NOT NULL,
    file_name   VARCHAR(190) NOT NULL DEFAULT '',
    size_bytes  BIGINT       NOT NULL DEFAULT 0,
    expires_on  DATE         NULL,
    uploaded_at DATETIME     NOT NULL,
    uploaded_by VARCHAR(190) NOT NULL DEFAULT '',
    INDEX idx_doc_user (user_id),
    CONSTRAINT fk_doc_user FOREIGN KEY (user_id) REFERENCES app_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS hrms_salary_structure (
    id                BIGINT AUTO_INCREMENT PRIMARY KEY,
    user_id           BIGINT        NOT NULL,
    effective_from    DATE          NOT NULL,
    basic             DECIMAL(12,2) NOT NULL DEFAULT 0,
    hra               DECIMAL(12,2) NOT NULL DEFAULT 0,
    conveyance        DECIMAL(12,2) NOT NULL DEFAULT 0,
    medical           DECIMAL(12,2) NOT NULL DEFAULT 0,
    special_allowance DECIMAL(12,2) NOT NULL DEFAULT 0,
    other_allowance   DECIMAL(12,2) NOT NULL DEFAULT 0,
    pf_applicable     TINYINT(1)    NOT NULL DEFAULT 1,
    esic_applicable   TINYINT(1)    NOT NULL DEFAULT 1,
    pt_applicable     TINYINT(1)    NOT NULL DEFAULT 1,
    created_at        TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by        VARCHAR(190)  NOT NULL DEFAULT '',
    UNIQUE KEY uq_sal_user_from (user_id, effective_from),
    CONSTRAINT fk_sal_user FOREIGN KEY (user_id) REFERENCES app_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS hrms_advances (
    id          BIGINT AUTO_INCREMENT PRIMARY KEY,
    user_id     BIGINT        NOT NULL,
    amount      DECIMAL(12,2) NOT NULL,
    recovered   DECIMAL(12,2) NOT NULL DEFAULT 0,
    per_month   DECIMAL(12,2) NOT NULL DEFAULT 0,
    given_on    DATE          NOT NULL,
    reason      VARCHAR(400)  NOT NULL DEFAULT '',
    status      ENUM('open','closed','cancelled') NOT NULL DEFAULT 'open',
    created_by  VARCHAR(190)  NOT NULL DEFAULT '',
    INDEX idx_adv_user (user_id, status),
    CONSTRAINT fk_adv_user FOREIGN KEY (user_id) REFERENCES app_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS hrms_incentives (
    id       BIGINT AUTO_INCREMENT PRIMARY KEY,
    user_id  BIGINT        NOT NULL,
    year     SMALLINT      NOT NULL,
    month    TINYINT       NOT NULL,
    amount   DECIMAL(12,2) NOT NULL DEFAULT 0,
    note     VARCHAR(400)  NOT NULL DEFAULT '',
    added_by VARCHAR(190)  NOT NULL DEFAULT '',
    UNIQUE KEY uq_inc (user_id, year, month),
    CONSTRAINT fk_inc_user FOREIGN KEY (user_id) REFERENCES app_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS hrms_payroll_runs (
    id           BIGINT AUTO_INCREMENT PRIMARY KEY,
    year         SMALLINT     NOT NULL,
    month        TINYINT      NOT NULL,
    status       ENUM('draft','finalised') NOT NULL DEFAULT 'draft',
    generated_at DATETIME     NOT NULL,
    generated_by VARCHAR(190) NOT NULL DEFAULT '',
    finalised_at DATETIME     NULL,
    UNIQUE KEY uq_run (year, month)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS hrms_payslips (
    id                BIGINT AUTO_INCREMENT PRIMARY KEY,
    run_id            BIGINT        NOT NULL,
    user_id           BIGINT        NOT NULL,
    working_days      DECIMAL(5,1)  NOT NULL DEFAULT 0,
    present_days      DECIMAL(5,1)  NOT NULL DEFAULT 0,
    paid_leave_days   DECIMAL(5,1)  NOT NULL DEFAULT 0,
    weekoff_days      DECIMAL(5,1)  NOT NULL DEFAULT 0,
    holiday_days      DECIMAL(5,1)  NOT NULL DEFAULT 0,
    lop_days          DECIMAL(5,1)  NOT NULL DEFAULT 0,
    paid_days         DECIMAL(5,1)  NOT NULL DEFAULT 0,
    basic             DECIMAL(12,2) NOT NULL DEFAULT 0,
    hra               DECIMAL(12,2) NOT NULL DEFAULT 0,
    conveyance        DECIMAL(12,2) NOT NULL DEFAULT 0,
    medical           DECIMAL(12,2) NOT NULL DEFAULT 0,
    special_allowance DECIMAL(12,2) NOT NULL DEFAULT 0,
    other_allowance   DECIMAL(12,2) NOT NULL DEFAULT 0,
    incentive         DECIMAL(12,2) NOT NULL DEFAULT 0,
    gross             DECIMAL(12,2) NOT NULL DEFAULT 0,
    pf_employee       DECIMAL(12,2) NOT NULL DEFAULT 0,
    pf_employer       DECIMAL(12,2) NOT NULL DEFAULT 0,
    esic_employee     DECIMAL(12,2) NOT NULL DEFAULT 0,
    esic_employer     DECIMAL(12,2) NOT NULL DEFAULT 0,
    professional_tax  DECIMAL(12,2) NOT NULL DEFAULT 0,
    advance_recovered DECIMAL(12,2) NOT NULL DEFAULT 0,
    other_deduction   DECIMAL(12,2) NOT NULL DEFAULT 0,
    total_deduction   DECIMAL(12,2) NOT NULL DEFAULT 0,
    net_pay           DECIMAL(12,2) NOT NULL DEFAULT 0,
    note              VARCHAR(400)  NOT NULL DEFAULT '',
    UNIQUE KEY uq_slip (run_id, user_id),
    INDEX idx_slip_user (user_id),
    CONSTRAINT fk_slip_run FOREIGN KEY (run_id) REFERENCES hrms_payroll_runs(id) ON DELETE CASCADE,
    CONSTRAINT fk_slip_user FOREIGN KEY (user_id) REFERENCES app_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
