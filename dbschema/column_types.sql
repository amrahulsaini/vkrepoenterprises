-- Column Types: standard field names our software understands
-- IDs must stay fixed (matched by switch in RecordsEditorWindow.MapColumns)
CREATE TABLE IF NOT EXISTS column_types (
  id         INT         PRIMARY KEY AUTO_INCREMENT,
  name       VARCHAR(100) NOT NULL UNIQUE,
  sort_order INT         DEFAULT 0
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT IGNORE INTO column_types (id, name, sort_order) VALUES
(1,  'Vehicle No',          1),
(2,  'Chassis No',          2),
(3,  'Model',               3),
(4,  'Engine No',           4),
(5,  'Agreement No',        5),
(6,  'Customer Name',       6),
(7,  'Customer Address',    7),
(8,  'Region',              8),
(9,  'Area',                9),
(10, 'Bucket',             10),
(11, 'GV',                 11),
(12, 'OD',                 12),
(13, 'Branch',             13),
(14, 'Level 1',            14),
(15, 'Level 1 Contact No', 15),
(16, 'Level 2',            16),
(17, 'Level 2 Contact No', 17),
(18, 'Level 3',            18),
(19, 'Level 3 Contact No', 19),
(20, 'Level 4',            20),
(21, 'Level 4 Contact No', 21),
(22, 'Sec 9 Available',    22),
(23, 'Sec 17 Available',   23),
(24, 'TBR Flag',           24),
(25, 'Seasoning',          25),
(26, 'Sender Mail Id 1',   26),
(27, 'Sender Mail Id 2',   27),
(28, 'Executive Name',     28),
(29, 'POS',                29),
(30, 'TOSS',               30),
(31, 'Customer Contact Nos', 31),
(32, 'Remark',             32);

-- Column Mappings: Excel header aliases → column_types
-- `name` is the normalized header (lowercase, alphanumeric only, same regex as MapColumns)
CREATE TABLE IF NOT EXISTS column_mappings (
  id              INT         PRIMARY KEY AUTO_INCREMENT,
  column_type_id  INT         NOT NULL,
  name            VARCHAR(150) NOT NULL COMMENT 'normalized: Regex.Replace(header,"[^A-Za-z0-9]","").ToLower()',
  UNIQUE KEY uq_name (name),
  INDEX idx_type  (column_type_id),
  FOREIGN KEY (column_type_id) REFERENCES column_types(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ─────────────────────────────────────────────────────────────────────────────
--  Vehicle No aliases  (column_type_id = 1)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT IGNORE INTO column_mappings (column_type_id, name) VALUES
(1,'rcno'),(1,'regdnum'),(1,'rcnumber'),(1,'vehiclenumber'),(1,'ragistrationnumber'),
(1,'vehicleno'),(1,'vehno'),(1,'regno'),(1,'registrationno'),(1,'assetno'),
(1,'vehiclercno'),(1,'vehid'),(1,'rcbookno'),(1,'registratonno'),(1,'vehiclercnumber'),
(1,'regnno'),(1,'registrationnumber'),(1,'vhiclnumbr'),(1,'registration'),(1,'regnumber'),
(1,'regnum'),(1,'vehiclesno'),(1,'regdno'),(1,'rcon'),(1,'vehiclenum'),
(1,'regitrationno'),(1,'reg'),(1,'vechileno'),(1,'regnoserialno'),(1,'regsitrationnumber'),
(1,'modal'),(1,'assetregnno'),(1,'veichelno'),(1,'regnnumber'),(1,'vehicleregistrationno'),
(1,'registeredno'),(1,'vheiclenumber'),(1,'vehregno'),(1,'registationno'),(1,'rc'),
(1,'minimumto'),(1,'vehicaleno'),(1,'regis'),(1,'regi'),(1,'regisno'),
(1,'jhk'),(1,'registrationnumber1'),(1,'vechicleno'),(1,'vehicalno'),(1,'assetidentityno'),
(1,'vehicleregistrationnumber'),(1,'vehicleregno'),(1,'vehcileno'),(1,'assetidentificationnumber'),
(1,'registraionnumber'),(1,'registerationno'),(1,'rtoregistrationnumber'),(1,'vegno'),
(1,'regitartionno'),(1,'dealn'),(1,'vehcleno'),(1,'vehiclenonew'),(1,'registernation'),
(1,'vechno'),(1,'reginumber'),(1,'regdnoslno'),(1,'regnnoserialno'),(1,'vehaleno'),
(1,'vehnum'),(1,'vehilceregno'),(1,'ragistrationno'),(1,'registernumber'),(1,'rcnon'),
(1,'vhno'),(1,'reqvehid');

-- ─────────────────────────────────────────────────────────────────────────────
--  Chassis No aliases  (column_type_id = 2)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT IGNORE INTO column_mappings (column_type_id, name) VALUES
(2,'chasisnum'),(2,'chassisno'),(2,'chesisnumber'),(2,'chasisno'),(2,'chassisnumber'),
(2,'engno'),(2,'chsno'),(2,'chasissno'),(2,'chassino'),(2,'chasisnumber'),
(2,'chassisnum'),(2,'chassicno'),(2,'chassisserialno'),(2,'cha'),(2,'vehiclechasisnumber'),
(2,'vehiclechassisno'),(2,'chassis'),(2,'chaseno'),(2,'chs'),(2,'chaissnumber'),
(2,'chasno'),(2,'vinnumber'),(2,'chessisno'),(2,'chaissesno'),(2,'chasis'),
(2,'chno'),(2,'chassesnumber'),(2,'chasino'),(2,'chassno'),(2,'chassiseno'),
(2,'chasison'),(2,'chasisinumber'),(2,'chasisnos'),(2,'chasisnm'),(2,'chassieno'),
(2,'chasisnumber1'),(2,'chassissno'),(2,'chessissno'),(2,'chesisno'),(2,'chaisseno'),
(2,'chassissnumber'),(2,'chachisno'),(2,'chaissess'),(2,'chessino'),(2,'chessis'),
(2,'chassisnoframeno'),(2,'chasisino'),(2,'vehiclechassisnumber'),(2,'chassiesno');

-- ─────────────────────────────────────────────────────────────────────────────
--  Model aliases  (column_type_id = 3)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT IGNORE INTO column_mappings (column_type_id, name) VALUES
(3,'make'),(3,'modelno'),(3,'material'),(3,'vehicledescription'),(3,'vehiclemake'),
(3,'model'),(3,'product'),(3,'assetdesc'),(3,'assdes'),(3,'assettype'),
(3,'asset'),(3,'assetcategory'),(3,'productmodel'),(3,'assetmodel'),(3,'vehiclemodel'),
(3,'year'),(3,'assetname'),(3,'description'),(3,'itemname'),(3,'productname'),
(3,'vehmodel'),(3,'vehicledescrption'),(3,'assetmake'),(3,'makemodel'),(3,'vehiclename'),
(3,'vehicletype'),(3,'vehiclemodelno'),(3,'jcb3dxbackhoeloader'),(3,'vehicle'),(3,'assetdescription'),
(3,'makeandmodel'),(3,'products'),(3,'vehicle5'),(3,'assts'),(3,'productcode'),
(3,'makemodename'),(3,'vehcilemodel'),(3,'vehcile'),(3,'vehciletype'),(3,'makemodal'),
(3,'modal1'),(3,'manufacturer'),(3,'productme'),(3,'manufacturemodel'),(3,'assetdetails'),
(3,'boleropickup'),(3,'verient'),(3,'variant'),(3,'vehicledesc'),(3,'vehiclemack'),
(3,'modelname'),(3,'mekandmodel'),(3,'productmodal'),(3,'produckt'),(3,'assettypedescription'),
(3,'user'),(3,'modelmake'),(3,'manufacturerdesc'),(3,'maker'),(3,'manufacturerdescmake'),
(3,'manufacturerder'),(3,'prod'),(3,'manufacturer1'),(3,'brandtypename'),(3,'manufacturerdescr'),
(3,'brandname'),(3,'vehicalmakemodel'),(3,'vehmakeandmodel'),(3,'vehiclemfgmodel'),
(3,'assetjuly2023'),(3,'assetmodel1'),(3,'productdisp'),(3,'prtoductname'),(3,'type'),
(3,'makemodelno'),(3,'proddetails'),(3,'vehiclemodelname'),(3,'makemodelregno'),
(3,'vehiclevarient'),(3,'carmodel'),(3,'assetmodelnew'),(3,'vechilemodel'),(3,'vehmake'),
(3,'productcategoryname'),(3,'assetmaketype'),(3,'vhclass'),(3,'vehicledetail');

-- ─────────────────────────────────────────────────────────────────────────────
--  Engine No aliases  (column_type_id = 4)
-- ─────────────────────────────────────────────────────────────────────────────
INSERT IGNORE INTO column_mappings (column_type_id, name) VALUES
(4,'enginenum'),(4,'engineno'),(4,'enginenumber'),(4,'engine1'),(4,'enegineno'),
(4,'engnno'),(4,'enginno'),(4,'engionno'),(4,'eng'),(4,'engine'),
(4,'engnum'),(4,'engieneno'),(4,'vehicleengineno'),(4,'enggno'),(4,'engno1'),
(4,'engeenno'),(4,'chssno'),(4,'enginenumer'),(4,'enginenumber1'),(4,'enginen'),
(4,'mh40l7597'),(4,'enginenos'),(4,'engnumber'),(4,'engion'),(4,'engino'),
(4,'enggnonumber'),(4,'vehicleenginenumber'),(4,'enginena'),(4,'enggineno');
