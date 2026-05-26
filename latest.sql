-- phpMyAdmin SQL Dump
-- version 5.2.1
-- https://www.phpmyadmin.net/
--
-- Host: localhost
-- Generation Time: May 11, 2026 at 10:04 AM
-- Server version: 10.11.16-MariaDB-ubu2204
-- PHP Version: 8.3.30

SET SQL_MODE = "NO_AUTO_VALUE_ON_ZERO";
START TRANSACTION;
SET time_zone = "+00:00";


/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;

--
-- Database: `vkre_db1`
--

-- --------------------------------------------------------

--
-- Table structure for table `app_users`
--

CREATE TABLE `app_users` (
  `id` bigint(20) NOT NULL,
  `mobile` varchar(15) NOT NULL,
  `name` varchar(150) NOT NULL,
  `address` text DEFAULT NULL,
  `pincode` varchar(10) DEFAULT NULL,
  `pfp` mediumtext DEFAULT NULL,
  `device_id` varchar(500) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 0,
  `is_admin` tinyint(1) NOT NULL DEFAULT 0,
  `balance` decimal(10,2) DEFAULT 0.00,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp(),
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `account_number` varchar(30) DEFAULT NULL,
  `ifsc_code` varchar(15) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Table structure for table `branches`
--

CREATE TABLE `branches` (
  `id` int(10) UNSIGNED NOT NULL,
  `finance_id` int(10) UNSIGNED NOT NULL,
  `name` varchar(255) NOT NULL,
  `address` text DEFAULT NULL,
  `contact` varchar(64) DEFAULT NULL,
  `uploaded_at` datetime DEFAULT NULL,
  `total_records` bigint(20) UNSIGNED NOT NULL DEFAULT 0,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp(),
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `contact1` varchar(64) DEFAULT NULL,
  `contact2` varchar(64) DEFAULT NULL,
  `contact3` varchar(64) DEFAULT NULL,
  `city` varchar(128) DEFAULT NULL,
  `state` varchar(128) DEFAULT NULL,
  `postal_code` varchar(32) DEFAULT NULL,
  `branch_code` varchar(64) DEFAULT NULL,
  `notes` text DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `chassis_info`
--

CREATE TABLE `chassis_info` (
  `id` bigint(20) UNSIGNED NOT NULL,
  `vehicle_record_id` bigint(20) UNSIGNED NOT NULL,
  `chassis_number` varchar(100) NOT NULL DEFAULT '',
  `model` varchar(200) NOT NULL DEFAULT '',
  `last5` char(5) NOT NULL DEFAULT ''
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `column_mappings`
--

CREATE TABLE `column_mappings` (
  `id` int(11) NOT NULL,
  `column_type_id` int(11) NOT NULL,
  `name` varchar(150) NOT NULL COMMENT 'normalized: Regex.Replace(header,"[^A-Za-z0-9]","").ToLower()'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `column_types`
--

CREATE TABLE `column_types` (
  `id` int(11) NOT NULL,
  `name` varchar(100) NOT NULL,
  `sort_order` int(11) DEFAULT 0
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `finances`
--

CREATE TABLE `finances` (
  `id` int(10) UNSIGNED NOT NULL,
  `name` varchar(255) NOT NULL,
  `slug` varchar(255) GENERATED ALWAYS AS (lcase(replace(`name`,' ','-'))) VIRTUAL,
  `description` text DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp(),
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `rc_info`
--

CREATE TABLE `rc_info` (
  `id` bigint(20) UNSIGNED NOT NULL,
  `vehicle_record_id` bigint(20) UNSIGNED NOT NULL,
  `rc_number` varchar(50) NOT NULL DEFAULT '',
  `model` varchar(200) NOT NULL DEFAULT '',
  `last4` char(4) NOT NULL DEFAULT ''
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `subscriptions`
--

CREATE TABLE `subscriptions` (
  `id` bigint(20) NOT NULL,
  `user_id` bigint(20) NOT NULL,
  `start_date` date NOT NULL,
  `end_date` date NOT NULL,
  `amount` decimal(10,2) DEFAULT NULL,
  `notes` varchar(300) DEFAULT NULL,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Table structure for table `user_kyc`
--

CREATE TABLE `user_kyc` (
  `id` bigint(20) NOT NULL,
  `user_id` bigint(20) NOT NULL,
  `aadhaar_front` longtext DEFAULT NULL,
  `aadhaar_back` longtext DEFAULT NULL,
  `pan_front` longtext DEFAULT NULL,
  `created_at` timestamp NULL DEFAULT current_timestamp(),
  `updated_at` timestamp NULL DEFAULT current_timestamp() ON UPDATE current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `vehicle_records`
--

CREATE TABLE `vehicle_records` (
  `id` bigint(20) UNSIGNED NOT NULL,
  `branch_id` int(10) UNSIGNED NOT NULL,
  `vehicle_no` varchar(50) DEFAULT NULL,
  `chassis_no` varchar(100) DEFAULT NULL,
  `engine_no` varchar(100) DEFAULT NULL,
  `model` varchar(200) DEFAULT NULL,
  `agreement_no` varchar(100) DEFAULT NULL,
  `bucket` varchar(50) DEFAULT NULL,
  `gv` varchar(50) DEFAULT NULL,
  `od` varchar(50) DEFAULT NULL,
  `seasoning` varchar(50) DEFAULT NULL,
  `tbr_flag` varchar(20) DEFAULT NULL,
  `sec9_available` varchar(20) DEFAULT NULL,
  `sec17_available` varchar(20) DEFAULT NULL,
  `customer_name` varchar(200) DEFAULT NULL,
  `customer_address` text DEFAULT NULL,
  `customer_contact` varchar(100) DEFAULT NULL,
  `owner_name` varchar(200) DEFAULT NULL,
  `mobile_no` varchar(50) DEFAULT NULL,
  `region` varchar(100) DEFAULT NULL,
  `area` varchar(100) DEFAULT NULL,
  `branch_name_raw` varchar(200) DEFAULT NULL,
  `level1` varchar(200) DEFAULT NULL,
  `level1_contact` varchar(100) DEFAULT NULL,
  `level2` varchar(200) DEFAULT NULL,
  `level2_contact` varchar(100) DEFAULT NULL,
  `level3` varchar(200) DEFAULT NULL,
  `level3_contact` varchar(100) DEFAULT NULL,
  `level4` varchar(200) DEFAULT NULL,
  `level4_contact` varchar(100) DEFAULT NULL,
  `sender_mail1` varchar(200) DEFAULT NULL,
  `sender_mail2` varchar(200) DEFAULT NULL,
  `executive_name` varchar(200) DEFAULT NULL,
  `pos` varchar(100) DEFAULT NULL,
  `toss` varchar(100) DEFAULT NULL,
  `remark` text DEFAULT NULL,
  `created_at` timestamp NULL DEFAULT current_timestamp()
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Stand-in structure for view `v_finance_summary`
-- (See below for the actual view)
--
CREATE TABLE `v_finance_summary` (
`finance_id` int(10) unsigned
,`finance_name` varchar(255)
,`description` text
,`created_at` timestamp
,`branch_count` bigint(21)
,`total_records` decimal(42,0)
);

-- --------------------------------------------------------

--
-- Table structure for table `_branch_ids`
--

CREATE TABLE `_branch_ids` (
  `pos` int(10) UNSIGNED NOT NULL,
  `bid` int(10) UNSIGNED NOT NULL
) ENGINE=MEMORY DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `_num10`
--

CREATE TABLE `_num10` (
  `n` tinyint(3) UNSIGNED NOT NULL
) ENGINE=MEMORY DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;

-- --------------------------------------------------------

--
-- Structure for view `v_finance_summary`
--
DROP TABLE IF EXISTS `v_finance_summary`;

CREATE ALGORITHM=UNDEFINED DEFINER=`root`@`localhost` SQL SECURITY DEFINER VIEW `v_finance_summary`  AS SELECT `f`.`id` AS `finance_id`, `f`.`name` AS `finance_name`, `f`.`description` AS `description`, `f`.`created_at` AS `created_at`, coalesce(`b`.`branch_count`,0) AS `branch_count`, coalesce(`b`.`total_records_sum`,0) AS `total_records` FROM (`finances` `f` left join (select `branches`.`finance_id` AS `finance_id`,count(0) AS `branch_count`,sum(`branches`.`total_records`) AS `total_records_sum` from `branches` where `branches`.`is_active` = 1 group by `branches`.`finance_id`) `b` on(`b`.`finance_id` = `f`.`id`)) ;

--
-- Indexes for dumped tables
--

--
-- Indexes for table `app_users`
--
ALTER TABLE `app_users`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_mobile` (`mobile`),
  ADD KEY `idx_device` (`device_id`(100));

--
-- Indexes for table `branches`
--
ALTER TABLE `branches`
  ADD PRIMARY KEY (`id`),
  ADD KEY `idx_branches_finance` (`finance_id`),
  ADD KEY `idx_branches_uploaded_at` (`uploaded_at`),
  ADD KEY `idx_branches_total_records` (`total_records`),
  ADD KEY `idx_branches_finance_name` (`finance_id`,`name`(100)),
  ADD KEY `idx_branches_branch_code` (`branch_code`(50)),
  ADD KEY `idx_branches_city` (`city`(50));
ALTER TABLE `branches` ADD FULLTEXT KEY `ft_branches_name` (`name`);

--
-- Indexes for table `chassis_info`
--
ALTER TABLE `chassis_info`
  ADD PRIMARY KEY (`id`),
  ADD KEY `idx_last5` (`last5`),
  ADD KEY `idx_chassis` (`chassis_number`),
  ADD KEY `idx_vr` (`vehicle_record_id`);

--
-- Indexes for table `column_mappings`
--
ALTER TABLE `column_mappings`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_name` (`name`),
  ADD KEY `idx_type` (`column_type_id`);

--
-- Indexes for table `column_types`
--
ALTER TABLE `column_types`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `name` (`name`);

--
-- Indexes for table `finances`
--
ALTER TABLE `finances`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_finances_name` (`name`),
  ADD KEY `idx_finances_created_at` (`created_at`);
ALTER TABLE `finances` ADD FULLTEXT KEY `ft_finances_name` (`name`);

--
-- Indexes for table `rc_info`
--
ALTER TABLE `rc_info`
  ADD PRIMARY KEY (`id`),
  ADD KEY `idx_last4` (`last4`),
  ADD KEY `idx_rc` (`rc_number`),
  ADD KEY `idx_vr` (`vehicle_record_id`);

--
-- Indexes for table `subscriptions`
--
ALTER TABLE `subscriptions`
  ADD PRIMARY KEY (`id`),
  ADD KEY `idx_user` (`user_id`);

--
-- Indexes for table `user_kyc`
--
ALTER TABLE `user_kyc`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_user` (`user_id`);

--
-- Indexes for table `vehicle_records`
--
ALTER TABLE `vehicle_records`
  ADD PRIMARY KEY (`id`),
  ADD KEY `idx_branch` (`branch_id`),
  ADD KEY `idx_vehicle_no` (`vehicle_no`),
  ADD KEY `idx_chassis_no` (`chassis_no`),
  ADD KEY `idx_vr_branch_id` (`branch_id`);

--
-- Indexes for table `_branch_ids`
--
ALTER TABLE `_branch_ids`
  ADD PRIMARY KEY (`pos`);

--
-- AUTO_INCREMENT for dumped tables
--

--
-- AUTO_INCREMENT for table `app_users`
--
ALTER TABLE `app_users`
  MODIFY `id` bigint(20) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `branches`
--
ALTER TABLE `branches`
  MODIFY `id` int(10) UNSIGNED NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `chassis_info`
--
ALTER TABLE `chassis_info`
  MODIFY `id` bigint(20) UNSIGNED NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `column_mappings`
--
ALTER TABLE `column_mappings`
  MODIFY `id` int(11) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `column_types`
--
ALTER TABLE `column_types`
  MODIFY `id` int(11) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `finances`
--
ALTER TABLE `finances`
  MODIFY `id` int(10) UNSIGNED NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `rc_info`
--
ALTER TABLE `rc_info`
  MODIFY `id` bigint(20) UNSIGNED NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `subscriptions`
--
ALTER TABLE `subscriptions`
  MODIFY `id` bigint(20) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `user_kyc`
--
ALTER TABLE `user_kyc`
  MODIFY `id` bigint(20) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `vehicle_records`
--
ALTER TABLE `vehicle_records`
  MODIFY `id` bigint(20) UNSIGNED NOT NULL AUTO_INCREMENT;

--
-- Constraints for dumped tables
--

--
-- Constraints for table `branches`
--
ALTER TABLE `branches`
  ADD CONSTRAINT `fk_branches_finances` FOREIGN KEY (`finance_id`) REFERENCES `finances` (`id`) ON DELETE CASCADE;

--
-- Constraints for table `chassis_info`
--
ALTER TABLE `chassis_info`
  ADD CONSTRAINT `fk_ch_vr` FOREIGN KEY (`vehicle_record_id`) REFERENCES `vehicle_records` (`id`) ON DELETE CASCADE;

--
-- Constraints for table `column_mappings`
--
ALTER TABLE `column_mappings`
  ADD CONSTRAINT `column_mappings_ibfk_1` FOREIGN KEY (`column_type_id`) REFERENCES `column_types` (`id`) ON DELETE CASCADE;

--
-- Constraints for table `rc_info`
--
ALTER TABLE `rc_info`
  ADD CONSTRAINT `fk_rc_vr` FOREIGN KEY (`vehicle_record_id`) REFERENCES `vehicle_records` (`id`) ON DELETE CASCADE;

--
-- Constraints for table `subscriptions`
--
ALTER TABLE `subscriptions`
  ADD CONSTRAINT `fk_sub_user` FOREIGN KEY (`user_id`) REFERENCES `app_users` (`id`) ON DELETE CASCADE;

--
-- Constraints for table `user_kyc`
--
ALTER TABLE `user_kyc`
  ADD CONSTRAINT `fk_kyc_user` FOREIGN KEY (`user_id`) REFERENCES `app_users` (`id`) ON DELETE CASCADE;

--
-- Constraints for table `vehicle_records`
--
ALTER TABLE `vehicle_records`
  ADD CONSTRAINT `fk_vehicle_branch` FOREIGN KEY (`branch_id`) REFERENCES `branches` (`id`) ON DELETE CASCADE;
COMMIT;

/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;