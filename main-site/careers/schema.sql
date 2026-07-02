SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS `careers_admins` (
  `id` int NOT NULL AUTO_INCREMENT,
  `user_name` varchar(50) NOT NULL,
  `password` varchar(255) NOT NULL,
  `series_id` varchar(60) DEFAULT NULL,
  `remember_token` varchar(255) DEFAULT NULL,
  `expires` datetime DEFAULT NULL,
  `admin_type` varchar(10) NOT NULL DEFAULT 'admin',
  PRIMARY KEY (`id`),
  UNIQUE KEY `user_name` (`user_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS `careers_jobs` (
  `id` int NOT NULL AUTO_INCREMENT,
  `job_title` varchar(100) NOT NULL,
  `department` varchar(100) NOT NULL,
  `vacancies` int NOT NULL DEFAULT 1,
  `employment_type` enum('Full Time','Part Time','Contract','Internship') NOT NULL,
  `work_mode` enum('On-site','Hybrid','Remote') NOT NULL,
  `location` varchar(150) NOT NULL,
  `experience` varchar(50) NOT NULL,
  `education` varchar(150) DEFAULT NULL,
  `skills` text NOT NULL,
  `salary` varchar(100) DEFAULT NULL,
  `application_deadline` date DEFAULT NULL,
  `job_description` text NOT NULL,
  `status` enum('Open','Draft','Closed','On Hold') NOT NULL DEFAULT 'Open',
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

CREATE TABLE IF NOT EXISTS `careers_applications` (
  `id` int NOT NULL AUTO_INCREMENT,
  `job_id` int NOT NULL,
  `job_title` varchar(100) NOT NULL,
  `full_name` varchar(150) NOT NULL,
  `email` varchar(150) NOT NULL,
  `phone` varchar(30) NOT NULL,
  `cover_letter` text,
  `resume_path` varchar(255) NOT NULL,
  `created_at` timestamp NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  KEY `job_id` (`job_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
