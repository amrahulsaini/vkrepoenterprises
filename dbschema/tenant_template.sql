/*M!999999\- enable the sandbox mode */ 

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;
DROP TABLE IF EXISTS `_branch_ids`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `_branch_ids` (
  `pos` int(10) unsigned NOT NULL,
  `bid` int(10) unsigned NOT NULL,
  PRIMARY KEY (`pos`)
) ENGINE=MEMORY DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `_num10`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `_num10` (
  `n` tinyint(3) unsigned NOT NULL
) ENGINE=MEMORY DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `app_settings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `app_settings` (
  `key` varchar(100) NOT NULL,
  `value` text DEFAULT NULL,
  `updated_at` datetime DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `app_users`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `app_users` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
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
  `ifsc_code` varchar(15) DEFAULT NULL,
  `last_seen` timestamp NULL DEFAULT NULL,
  `last_lat` decimal(10,8) DEFAULT NULL,
  `last_lng` decimal(11,8) DEFAULT NULL,
  `is_stopped` tinyint(1) NOT NULL DEFAULT 0,
  `is_blacklisted` tinyint(1) NOT NULL DEFAULT 0,
  `admin_pass` varchar(100) DEFAULT NULL,
  `kyc_aadhaar_last4` varchar(8) DEFAULT NULL,
  `kyc_aadhaar_name` varchar(190) DEFAULT NULL,
  `kyc_aadhaar_dob` varchar(20) DEFAULT NULL,
  `kyc_aadhaar_gender` varchar(20) DEFAULT NULL,
  `kyc_aadhaar_address` text DEFAULT NULL,
  `kyc_aadhaar_verified` tinyint(1) NOT NULL DEFAULT 0,
  `kyc_pan` varchar(12) DEFAULT NULL,
  `kyc_pan_name` varchar(190) DEFAULT NULL,
  `kyc_pan_verified` tinyint(1) NOT NULL DEFAULT 0,
  `kyc_bank_holder` varchar(190) DEFAULT NULL,
  `kyc_bank_verified` tinyint(1) NOT NULL DEFAULT 0,
  `kyc_reg_lat` double DEFAULT NULL,
  `kyc_reg_lng` double DEFAULT NULL,
  `kyc_reg_location` varchar(255) DEFAULT NULL,
  `kyc_verified_at` datetime DEFAULT NULL,
  `kyc_status` varchar(20) NOT NULL DEFAULT 'pending',
  `kyc_reject_note` text DEFAULT NULL,
  `kyc_aadhaar_number` varchar(12) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_mobile` (`mobile`),
  KEY `idx_device` (`device_id`(100))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `branches`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `branches` (
  `id` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `finance_id` int(10) unsigned NOT NULL,
  `name` varchar(255) NOT NULL,
  `address` text DEFAULT NULL,
  `contact` varchar(255) DEFAULT NULL,
  `uploaded_at` datetime DEFAULT NULL,
  `total_records` bigint(20) unsigned NOT NULL DEFAULT 0,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp(),
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `contact1` varchar(255) DEFAULT NULL,
  `contact2` varchar(255) DEFAULT NULL,
  `contact3` varchar(255) DEFAULT NULL,
  `city` varchar(128) DEFAULT NULL,
  `state` varchar(128) DEFAULT NULL,
  `postal_code` varchar(32) DEFAULT NULL,
  `branch_code` varchar(64) DEFAULT NULL,
  `notes` text DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_branches_finance` (`finance_id`),
  KEY `idx_branches_uploaded_at` (`uploaded_at`),
  KEY `idx_branches_total_records` (`total_records`),
  KEY `idx_branches_finance_name` (`finance_id`,`name`(100)),
  KEY `idx_branches_branch_code` (`branch_code`(50)),
  KEY `idx_branches_city` (`city`(50)),
  FULLTEXT KEY `ft_branches_name` (`name`),
  CONSTRAINT `fk_branches_finances` FOREIGN KEY (`finance_id`) REFERENCES `finances` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `integration_agency_messages`;
CREATE TABLE `integration_agency_messages` (
  `id` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `integration_account_id` int(11) NOT NULL,
  `from_finance_name` varchar(200) DEFAULT NULL,
  `from_email` varchar(200) DEFAULT NULL,
  `message` text NOT NULL,
  `is_read` tinyint(1) NOT NULL DEFAULT 0,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  KEY `idx_iam_created` (`created_at`),
  KEY `idx_iam_read` (`is_read`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
DROP TABLE IF EXISTS `integration_uploads`;
CREATE TABLE `integration_uploads` (
  `id` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `finance_id` int(10) unsigned NOT NULL,
  `branch_id` int(10) unsigned NOT NULL,
  `uploaded_by` varchar(200) DEFAULT NULL,
  `file_name` varchar(500) DEFAULT NULL,
  `file_path` varchar(1000) DEFAULT NULL,
  `total_records` int(11) NOT NULL DEFAULT 0,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  KEY `idx_iu_finance` (`finance_id`),
  KEY `idx_iu_branch` (`branch_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
DROP TABLE IF EXISTS `chassis_info`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `chassis_info` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `vehicle_record_id` bigint(20) unsigned NOT NULL,
  `chassis_number` varchar(100) NOT NULL DEFAULT '',
  `model` varchar(200) NOT NULL DEFAULT '',
  `last5` char(5) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`),
  KEY `idx_last5` (`last5`),
  KEY `idx_chassis` (`chassis_number`),
  KEY `idx_vr` (`vehicle_record_id`),
  CONSTRAINT `fk_ch_vr` FOREIGN KEY (`vehicle_record_id`) REFERENCES `vehicle_records` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `column_mappings`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `column_mappings` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `column_type_id` int(11) NOT NULL,
  `name` varchar(150) NOT NULL COMMENT 'normalized: Regex.Replace(header,"[^A-Za-z0-9]","").ToLower()',
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_name` (`name`),
  KEY `idx_type` (`column_type_id`),
  CONSTRAINT `column_mappings_ibfk_1` FOREIGN KEY (`column_type_id`) REFERENCES `column_types` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `column_types`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `column_types` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(100) NOT NULL,
  `sort_order` int(11) DEFAULT 0,
  PRIMARY KEY (`id`),
  UNIQUE KEY `name` (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `device_change_requests`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `device_change_requests` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `user_id` bigint(20) NOT NULL,
  `user_name` varchar(150) NOT NULL DEFAULT '',
  `user_mobile` varchar(15) NOT NULL DEFAULT '',
  `new_device_id` varchar(500) NOT NULL DEFAULT '',
  `requested_at` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_user` (`user_id`),
  CONSTRAINT `fk_dcr_user` FOREIGN KEY (`user_id`) REFERENCES `app_users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `finances`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `finances` (
  `id` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `name` varchar(255) NOT NULL,
  `slug` varchar(255) GENERATED ALWAYS AS (lcase(replace(`name`,' ','-'))) VIRTUAL,
  `description` text DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp(),
  `updated_at` timestamp NOT NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_finances_name` (`name`),
  KEY `idx_finances_created_at` (`created_at`),
  FULLTEXT KEY `ft_finances_name` (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `rc_info`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `rc_info` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `vehicle_record_id` bigint(20) unsigned NOT NULL,
  `rc_number` varchar(50) NOT NULL DEFAULT '',
  `model` varchar(200) NOT NULL DEFAULT '',
  `last4` char(4) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`),
  KEY `idx_last4` (`last4`),
  KEY `idx_rc` (`rc_number`),
  KEY `idx_vr` (`vehicle_record_id`),
  CONSTRAINT `fk_rc_vr` FOREIGN KEY (`vehicle_record_id`) REFERENCES `vehicle_records` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `search_logs`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `search_logs` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `user_id` bigint(20) NOT NULL,
  `vehicle_no` varchar(50) NOT NULL DEFAULT '',
  `chassis_no` varchar(100) NOT NULL DEFAULT '',
  `model` varchar(200) NOT NULL DEFAULT '',
  `lat` decimal(9,6) DEFAULT NULL,
  `lng` decimal(9,6) DEFAULT NULL,
  `address` text DEFAULT NULL,
  `device_time` datetime NOT NULL,
  `server_time` timestamp NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  KEY `idx_sl_user` (`user_id`),
  KEY `idx_sl_vehicle` (`vehicle_no`),
  KEY `idx_sl_server_time` (`server_time`),
  CONSTRAINT `fk_sl_user` FOREIGN KEY (`user_id`) REFERENCES `app_users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `subscriptions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `subscriptions` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `user_id` bigint(20) NOT NULL,
  `start_date` date NOT NULL,
  `end_date` date NOT NULL,
  `amount` decimal(10,2) DEFAULT NULL,
  `notes` varchar(300) DEFAULT NULL,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  KEY `idx_user` (`user_id`),
  CONSTRAINT `fk_sub_user` FOREIGN KEY (`user_id`) REFERENCES `app_users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `user_finance_restrictions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `user_finance_restrictions` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `user_id` bigint(20) NOT NULL,
  `finance_id` int(11) NOT NULL,
  `created_at` datetime DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_user_finance` (`user_id`,`finance_id`),
  KEY `idx_ufr_user` (`user_id`),
  CONSTRAINT `fk_ufr_user` FOREIGN KEY (`user_id`) REFERENCES `app_users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `user_kyc`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `user_kyc` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `user_id` bigint(20) NOT NULL,
  `aadhaar_front` longtext DEFAULT NULL,
  `aadhaar_back` longtext DEFAULT NULL,
  `pan_front` longtext DEFAULT NULL,
  `created_at` timestamp NULL DEFAULT current_timestamp(),
  `updated_at` timestamp NULL DEFAULT current_timestamp() ON UPDATE current_timestamp(),
  `selfie` varchar(255) DEFAULT NULL,
  `aadhaar_photo` varchar(255) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_user` (`user_id`),
  CONSTRAINT `fk_kyc_user` FOREIGN KEY (`user_id`) REFERENCES `app_users` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `v_finance_summary`;
/*!50001 DROP VIEW IF EXISTS `v_finance_summary`*/;
SET @saved_cs_client     = @@character_set_client;
SET character_set_client = utf8mb4;
/*!50001 CREATE VIEW `v_finance_summary` AS SELECT
 NULL AS `finance_id`,
 NULL AS `finance_name`,
 NULL AS `description`,
 NULL AS `created_at`,
 NULL AS `branch_count`,
 NULL AS `total_records` */;
SET character_set_client = @saved_cs_client;
DROP TABLE IF EXISTS `vehicle_records`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `vehicle_records` (
  `id` bigint(20) unsigned NOT NULL AUTO_INCREMENT,
  `branch_id` int(10) unsigned NOT NULL,
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
  `created_at` timestamp NULL DEFAULT current_timestamp(),
  `upload_id` int(10) unsigned DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `idx_branch` (`branch_id`),
  KEY `idx_vehicle_no` (`vehicle_no`),
  KEY `idx_chassis_no` (`chassis_no`),
  KEY `idx_vr_branch_id` (`branch_id`),
  CONSTRAINT `fk_vehicle_branch` FOREIGN KEY (`branch_id`) REFERENCES `branches` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `webhook_banks`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `webhook_banks` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `bank_name` varchar(255) NOT NULL,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_bank_name` (`bank_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `webhook_files`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `webhook_files` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `bank_id` int(11) NOT NULL,
  `file_name` varchar(500) NOT NULL,
  `file_path` varchar(1000) DEFAULT NULL,
  `vehicle_type` varchar(100) DEFAULT NULL,
  `uploaded_by` varchar(255) DEFAULT NULL,
  `uploaded_date` varchar(100) DEFAULT NULL,
  `file_guid` varchar(255) DEFAULT NULL,
  `total_records` int(11) NOT NULL DEFAULT 0,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  KEY `idx_wf_bank` (`bank_id`),
  KEY `idx_wf_created` (`created_at`),
  CONSTRAINT `fk_wf_bank` FOREIGN KEY (`bank_id`) REFERENCES `webhook_banks` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `webhook_users`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `webhook_users` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `username` varchar(255) NOT NULL,
  `password_hash` varchar(64) NOT NULL,
  `created_at` timestamp NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_username` (`username`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!50003 SET @saved_sql_mode       = @@sql_mode */ ;
/*!50003 SET sql_mode              = 'STRICT_TRANS_TABLES,ERROR_FOR_DIVISION_BY_ZERO,NO_AUTO_CREATE_USER,NO_ENGINE_SUBSTITUTION' */ ;
/*!50003 DROP PROCEDURE IF EXISTS `sp_create_branch` */;
/*!50003 SET @saved_cs_client      = @@character_set_client */ ;
/*!50003 SET @saved_cs_results     = @@character_set_results */ ;
/*!50003 SET @saved_col_connection = @@collation_connection */ ;
/*!50003 SET character_set_client  = utf8mb4 */ ;
/*!50003 SET character_set_results = utf8mb4 */ ;
/*!50003 SET collation_connection  = utf8mb4_unicode_ci */ ;
DELIMITER ;;
CREATE DEFINER=`tu_v_k_enterprises`@`localhost` PROCEDURE `sp_create_branch`(
  IN p_finance_id INT,
  IN p_name VARCHAR(255),
  IN p_address TEXT,
  IN p_contact VARCHAR(64),
  OUT p_id INT
)
BEGIN
  START TRANSACTION;
  INSERT INTO `branches` (`finance_id`, `name`, `address`, `contact`) VALUES (p_finance_id, p_name, p_address, p_contact);
  SET p_id = LAST_INSERT_ID();
  COMMIT;
END
;;
DELIMITER ;
/*!50003 SET sql_mode              = @saved_sql_mode */ ;
/*!50003 SET character_set_client  = @saved_cs_client */ ;
/*!50003 SET character_set_results = @saved_cs_results */ ;
/*!50003 SET collation_connection  = @saved_col_connection */ ;
/*!50003 SET @saved_sql_mode       = @@sql_mode */ ;
/*!50003 SET sql_mode              = 'STRICT_TRANS_TABLES,ERROR_FOR_DIVISION_BY_ZERO,NO_AUTO_CREATE_USER,NO_ENGINE_SUBSTITUTION' */ ;
/*!50003 DROP PROCEDURE IF EXISTS `sp_create_finance` */;
/*!50003 SET @saved_cs_client      = @@character_set_client */ ;
/*!50003 SET @saved_cs_results     = @@character_set_results */ ;
/*!50003 SET @saved_col_connection = @@collation_connection */ ;
/*!50003 SET character_set_client  = utf8mb4 */ ;
/*!50003 SET character_set_results = utf8mb4 */ ;
/*!50003 SET collation_connection  = utf8mb4_unicode_ci */ ;
DELIMITER ;;
CREATE DEFINER=`tu_v_k_enterprises`@`localhost` PROCEDURE `sp_create_finance`(
  IN p_name VARCHAR(255),
  IN p_description TEXT,
  OUT p_id INT
)
BEGIN
  INSERT INTO `finances` (`name`, `description`) VALUES (p_name, p_description);
  SET p_id = LAST_INSERT_ID();
END
;;
DELIMITER ;
/*!50003 SET sql_mode              = @saved_sql_mode */ ;
/*!50003 SET character_set_client  = @saved_cs_client */ ;
/*!50003 SET character_set_results = @saved_cs_results */ ;
/*!50003 SET collation_connection  = @saved_col_connection */ ;
/*!50003 SET @saved_sql_mode       = @@sql_mode */ ;
/*!50003 SET sql_mode              = 'STRICT_TRANS_TABLES,ERROR_FOR_DIVISION_BY_ZERO,NO_AUTO_CREATE_USER,NO_ENGINE_SUBSTITUTION' */ ;
/*!50003 DROP PROCEDURE IF EXISTS `sp_seed_vehicles` */;
/*!50003 SET @saved_cs_client      = @@character_set_client */ ;
/*!50003 SET @saved_cs_results     = @@character_set_results */ ;
/*!50003 SET @saved_col_connection = @@collation_connection */ ;
/*!50003 SET character_set_client  = utf8mb4 */ ;
/*!50003 SET character_set_results = utf8mb4 */ ;
/*!50003 SET collation_connection  = utf8mb4_general_ci */ ;
DELIMITER ;;
CREATE DEFINER=`tu_v_k_enterprises`@`localhost` PROCEDURE `sp_seed_vehicles`(
    IN p_offset       BIGINT UNSIGNED,
    IN p_count        BIGINT UNSIGNED,
    IN p_branch_count INT UNSIGNED
)
BEGIN
    SET @off = p_offset;
    SET @bc  = p_branch_count;

    INSERT INTO vehicle_records
        (branch_id, vehicle_no, chassis_no, engine_no, model,
         agreement_no, bucket, customer_name, customer_contact,
         mobile_no, region, area, executive_name, pos, toss,
         tbr_flag, sec9_available, sec17_available)
    SELECT
        (SELECT bid FROM _branch_ids WHERE pos = (n.rownum % @bc) LIMIT 1),

        CONCAT(
            ELT((n.rownum % 10)+1,'RJ','MH','DL','GJ','KA','TN','UP','WB','MP','HR'),
            LPAD(((n.rownum % 35)+1)*2, 2, '0'),
            CHAR(65 + (n.rownum % 26)),
            CHAR(65 + ((n.rownum + 7) % 26)),
            LPAD(((@off + n.rownum) % 9000)+1000, 4, '0')
        ),

        CONCAT(
            CHAR(65 + ((@off + n.rownum) % 26)),
            CHAR(65 + (((@off + n.rownum) DIV 26) % 26)),
            CHAR(65 + (((@off + n.rownum) DIV 676) % 26)),
            LPAD((@off + n.rownum) % 100000000, 8, '0'),
            CHAR(65 + ((n.rownum + 5) % 26)),
            LPAD(n.rownum % 99999, 5, '0'),
            CHAR(48 + (n.rownum % 10))
        ),

        CONCAT('ENG', LPAD(@off + n.rownum, 12, '0')),

        ELT((n.rownum % 10)+1,
            'Honda Activa 6G','TVS Apache RTR 160','Bajaj Pulsar NS200',
            'Hero Splendor Plus','Royal Enfield Classic 350','Maruti Swift Dzire',
            'Hyundai i20 Asta','Tata Nexon EV','Mahindra Scorpio N','Toyota Innova Crysta'),

        CONCAT('AGR', LPAD(@off + n.rownum, 10, '0')),

        ELT((n.rownum % 5)+1,'NPA','X','1-30','31-60','61-90'),

        CONCAT(
            ELT((n.rownum % 10)+1,'Rajesh','Priya','Amit','Sunita','Vijay','Kavita','Ravi','Meena','Suresh','Anita'),
            ' ',
            ELT(((n.rownum+3) % 10)+1,'Sharma','Verma','Patel','Gupta','Singh','Joshi','Kumar','Yadav','Mehta','Chauhan')
        ),

        CONCAT('9', LPAD((@off + n.rownum) % 1000000000, 9, '0')),
        CONCAT('8', LPAD((@off + n.rownum + 500000000) % 1000000000, 9, '0')),

        ELT((n.rownum % 6)+1,'North','South','East','West','Central','Northeast'),
        ELT((n.rownum % 8)+1,'Urban','Semi-Urban','Rural','Metro','Tier-1','Tier-2','Tier-3','Industrial'),

        CONCAT(
            ELT((n.rownum % 8)+1,'Ankit','Pooja','Rahul','Neha','Sanjay','Divya','Mohit','Shreya'),
            ' ',
            ELT(((n.rownum+2) % 8)+1,'Agarwal','Mehta','Chauhan','Tiwari','Pandey','Saxena','Mishra','Dubey')
        ),

        CONCAT(LPAD(FLOOR(10000 + ((@off + n.rownum) % 990000)), 6, '0'), '.00'),
        CONCAT(LPAD(FLOOR(500 + (n.rownum % 50000)), 5, '0'), '.00'),

        ELT((n.rownum % 2)+1,'Y','N'),
        ELT((n.rownum % 3)+1,'Y','N','Pending'),
        ELT(((n.rownum+1) % 3)+1,'Y','N','Pending')

    FROM (
        SELECT (a.n + b.n*10 + c.n*100 + d.n*1000 + e.n*10000 + f.n*100000 + g.n*1000000) AS rownum
        FROM _num10 a, _num10 b, _num10 c, _num10 d, _num10 e, _num10 f, _num10 g
        LIMIT 10000000
    ) n
    WHERE n.rownum < p_count;
END
;;
DELIMITER ;
/*!50003 SET sql_mode              = @saved_sql_mode */ ;
/*!50003 SET character_set_client  = @saved_cs_client */ ;
/*!50003 SET character_set_results = @saved_cs_results */ ;
/*!50003 SET collation_connection  = @saved_col_connection */ ;
/*!50001 DROP VIEW IF EXISTS `v_finance_summary`*/;
/*!50001 SET @saved_cs_client          = @@character_set_client */;
/*!50001 SET @saved_cs_results         = @@character_set_results */;
/*!50001 SET @saved_col_connection     = @@collation_connection */;
/*!50001 SET character_set_client      = utf8mb4 */;
/*!50001 SET character_set_results     = utf8mb4 */;
/*!50001 SET collation_connection      = utf8mb4_unicode_ci */;
/*!50001 CREATE ALGORITHM=UNDEFINED */
/*!50013 DEFINER=`tu_v_k_enterprises`@`localhost` SQL SECURITY DEFINER */
/*!50001 VIEW `v_finance_summary` AS select `f`.`id` AS `finance_id`,`f`.`name` AS `finance_name`,`f`.`description` AS `description`,`f`.`created_at` AS `created_at`,coalesce(`b`.`branch_count`,0) AS `branch_count`,coalesce(`b`.`total_records_sum`,0) AS `total_records` from (`finances` `f` left join (select `branches`.`finance_id` AS `finance_id`,count(0) AS `branch_count`,sum(`branches`.`total_records`) AS `total_records_sum` from `branches` where `branches`.`is_active` = 1 group by `branches`.`finance_id`) `b` on(`b`.`finance_id` = `f`.`id`)) */;
/*!50001 SET character_set_client      = @saved_cs_client */;
/*!50001 SET character_set_results     = @saved_cs_results */;
/*!50001 SET collation_connection      = @saved_col_connection */;
DROP TABLE IF EXISTS `repo_letter_settings`;
CREATE TABLE `repo_letter_settings` (
  `finance_id`     INT          NOT NULL DEFAULT 0,
  `agency_name`    VARCHAR(255) NULL,
  `authorized_by`  VARCHAR(255) NULL,
  `police_station` VARCHAR(255) NULL,
  `police_address` TEXT         NULL,
  `logo_path`      VARCHAR(512) NULL,
  `updated_at`     TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`finance_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

DROP TABLE IF EXISTS `billing_settings`;
CREATE TABLE `billing_settings` (
  `finance_id`        INT          NOT NULL DEFAULT 0,
  `agency_name`       VARCHAR(255) NULL,
  `header_address`    VARCHAR(512) NULL,
  `header_contact`    VARCHAR(255) NULL,
  `header_email`      VARCHAR(255) NULL,
  `pan_no`            VARCHAR(32)  NULL,
  `gst_state`         VARCHAR(64)  NULL,
  `bank_account_name` VARCHAR(255) NULL,
  `account_no`        VARCHAR(64)  NULL,
  `ifsc_code`         VARCHAR(32)  NULL,
  `bank_branch`       VARCHAR(128) NULL,
  `parking_yard`      VARCHAR(255) NULL,
  `payment_name`      VARCHAR(255) NULL,
  `footer_line`       VARCHAR(255) NULL,
  `logo_path`         VARCHAR(512) NULL,
  `letterhead_path`   VARCHAR(512) NULL,
  `background_path`   VARCHAR(512) NULL,
  `updated_at`        TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`finance_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS `repo_submissions` (
  `id`                     BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `record_id`              BIGINT UNSIGNED NULL,
  `finance_id`             INT UNSIGNED    NULL,
  `finance_name`           VARCHAR(255)    NULL,
  `branch_name`            VARCHAR(255)    NULL,
  `loan_no`                VARCHAR(128)    NULL,
  `customer_name`          VARCHAR(255)    NULL,
  `vehicle_no`             VARCHAR(64)     NULL,
  `model`                  VARCHAR(255)    NULL,
  `chassis_no`             VARCHAR(128)    NULL,
  `engine_no`              VARCHAR(128)    NULL,
  `agent_name`             VARCHAR(255)    NULL,
  `parking_yard_name`      VARCHAR(255)    NULL,
  `parking_yard_mobile`    VARCHAR(64)     NULL,
  `load_details`           VARCHAR(512)    NULL,
  `addl_charges_notes`     VARCHAR(512)    NULL,
  `addl_charges_amount`    DECIMAL(12,2)   NULL,
  `confirmation_by_name`   VARCHAR(255)    NULL,
  `confirmation_by_mobile` VARCHAR(64)     NULL,
  `executive_name`         VARCHAR(255)    NULL,
  `collection_update`      VARCHAR(512)    NULL,
  `remark`                 VARCHAR(512)    NULL,
  `billing_action`         ENUM('immediate','hold','cancel') NOT NULL DEFAULT 'immediate',
  `hold_until`             DATE            NULL,
  `hold_days`              INT             NULL,
  `bill_status`            ENUM('pending','billed') NOT NULL DEFAULT 'pending',
  `billed_at`              TIMESTAMP       NULL,
  `billed_by_member_id`    BIGINT UNSIGNED NULL,
  `submitted_by_user_id`   BIGINT          NULL,
  `submitted_by_name`      VARCHAR(255)    NULL,
  `created_at`             TIMESTAMP       NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `idx_repo_finance` (`finance_id`),
  KEY `idx_repo_created` (`created_at`),
  KEY `idx_repo_action`  (`billing_action`),
  KEY `idx_repo_status`  (`bill_status`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS `billing_members` (
  `id`         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
  `name`       VARCHAR(255) NOT NULL,
  `mobile`     VARCHAR(64)  NULL,
  `email`      VARCHAR(255) NULL,
  `username`   VARCHAR(128) NOT NULL,
  `password`   VARCHAR(255) NOT NULL,
  `is_active`  TINYINT(1)   NOT NULL DEFAULT 1,
  `created_at` TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_billing_member_username` (`username`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS `billing_member_finances` (
  `member_id`  BIGINT UNSIGNED NOT NULL,
  `finance_id` INT UNSIGNED    NOT NULL,
  PRIMARY KEY (`member_id`, `finance_id`),
  UNIQUE KEY `uq_bmf_finance` (`finance_id`),
  CONSTRAINT `fk_bmf_member` FOREIGN KEY (`member_id`) REFERENCES `billing_members` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;


-- confirm_captures — "Send Confirm" photos captured by field agents.
CREATE TABLE IF NOT EXISTS confirm_captures (
    id          BIGINT       NOT NULL AUTO_INCREMENT,
    user_id     BIGINT       NOT NULL,
    vehicle_no  VARCHAR(32)           DEFAULT NULL,
    chassis_no  VARCHAR(40)           DEFAULT NULL,
    image_path  VARCHAR(255) NOT NULL,
    captured_at DATETIME              DEFAULT NULL,
    created_at  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id),
    INDEX idx_cc_user    (user_id),
    INDEX idx_cc_vehicle (vehicle_no),
    INDEX idx_cc_chassis (chassis_no)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- id_cards — agent ID-card requests & approvals (one row per user).
CREATE TABLE IF NOT EXISTS id_cards (
    user_id        BIGINT       NOT NULL,
    photo_path     VARCHAR(255)          DEFAULT NULL,
    pcc_path       VARCHAR(255)          DEFAULT NULL,
    dra_path       VARCHAR(255)          DEFAULT NULL,
    blood_group    VARCHAR(8)            DEFAULT NULL,
    dob            VARCHAR(20)           DEFAULT NULL,
    status         VARCHAR(12)  NOT NULL DEFAULT 'pending',
    decline_reason TEXT                  DEFAULT NULL,
    valid_days     INT                   DEFAULT NULL,
    approved_at    DATETIME              DEFAULT NULL,
    valid_until    DATE                  DEFAULT NULL,
    submitted_at   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at     TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (user_id),
    INDEX idx_idcard_status (status),
    CONSTRAINT fk_idcard_user FOREIGN KEY (user_id)
        REFERENCES app_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
