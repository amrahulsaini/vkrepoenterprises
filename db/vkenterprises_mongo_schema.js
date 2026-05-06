// vkenterprises_mongo_schema.js
// Usage:
//   mongosh "mongodb://localhost:27017/vkenterprises" --file vkenterprises_mongo_schema.js

print('Running vkenterprises_mongo_schema.js on ' + db.getName());

function applyValidator(collName, jsonSchema) {
  if (db.getCollectionNames().includes(collName)) {
    print(`Updating validator for collection: ${collName}`);
    try {
      db.runCommand({
        collMod: collName,
        validator: { $jsonSchema: jsonSchema },
        validationLevel: 'moderate',   // 'moderate' won't block existing docs
        validationAction: 'warn'       // 'warn' only logs; change to 'error' to enforce
      });
    } catch (e) {
      print('collMod failed for ' + collName + ': ' + e);
    }
  } else {
    print(`Creating collection with validator: ${collName}`);
    try {
      db.createCollection(collName, {
        validator: { $jsonSchema: jsonSchema },
        validationLevel: 'moderate',
        validationAction: 'warn'
      });
    } catch (e) {
      print('createCollection failed for ' + collName + ': ' + e);
    }
  }
}

// Reusable numeric type union so validators accept int/long/double/decimal
const numericTypes = ["int", "long", "double", "decimal"];

// 1) appusers
const appUsersSchema = {
  bsonType: "object",
  required: ["AppUserId", "MobileNo", "FullName", "IsActive", "IsAdmin", "CreatedOn"],
  properties: {
    AppUserId: { bsonType: numericTypes, description: "numeric id" },
    MobileNo: { bsonType: "string", description: "mobile number" },
    FirstName: { bsonType: "string" },
    LastName: { bsonType: "string" },
    FullName: { bsonType: "string" },
    IsActive: { bsonType: "bool" },
    IsAdmin: { bsonType: "bool" },
    CreatedOn: { bsonType: "date" },
    ProfileImage: { bsonType: ["string", "null"] }
  }
};
applyValidator('appusers', appUsersSchema);

// 2) branches
const branchesSchema = {
  bsonType: "object",
  required: ["BranchId","Name"],
  properties: {
    BranchId: { bsonType: numericTypes },
    Name: { bsonType: "string" },
    City: { bsonType: "string" }
  }
};
applyValidator('branches', branchesSchema);

// 3) records
const recordsSchema = {
  bsonType: "object",
  required: ["RecordId","VehicleNo","CreatedOn"],
  properties: {
    RecordId: { bsonType: numericTypes },
    VehicleNo: { bsonType: "string" },
    OwnerName: { bsonType: "string" },
    ChassisNo: { bsonType: "string" },
    EngineNo: { bsonType: "string" },
    BranchId: { bsonType: numericTypes },
    BranchName: { bsonType: "string" },
    CreatedOn: { bsonType: "date" }
  }
};
applyValidator('records', recordsSchema);

// 4) finances
const financesSchema = {
  bsonType: "object",
  required: ["FinanceId","VehicleNo","Amount","CreatedOn"],
  properties: {
    FinanceId: { bsonType: numericTypes },
    VehicleNo: { bsonType: "string" },
    FinanceType: { bsonType: "string" },
    Amount: { bsonType: ["double","decimal","int","long"] },
    FinanceDate: { bsonType: "date" },
    CreatedOn: { bsonType: "date" }
  }
};
applyValidator('finances', financesSchema);

// 5) confirmations
const confirmationsSchema = {
  bsonType: "object",
  required: ["ConfirmationId","VehicleNo","Status","CreatedOn"],
  properties: {
    ConfirmationId: { bsonType: numericTypes },
    VehicleNo: { bsonType: "string" },
    Status: { bsonType: "string" },
    AppUserId: { bsonType: numericTypes },
    CreatedOn: { bsonType: "date" }
  }
};
applyValidator('confirmations', confirmationsSchema);

// 6) feedbacks
const feedbacksSchema = {
  bsonType: "object",
  required: ["FeedbackId","AppUserId","Subject","CreatedOn"],
  properties: {
    FeedbackId: { bsonType: numericTypes },
    AppUserId: { bsonType: numericTypes },
    Subject: { bsonType: "string" },
    Status: { bsonType: "string" },
    CreatedOn: { bsonType: "date" }
  }
};
applyValidator('feedbacks', feedbacksSchema);

// 7) payment_methods
const paymentMethodsSchema = {
  bsonType: "object",
  required: ["PaymentMethodId","MethodName","IsActive"],
  properties: {
    PaymentMethodId: { bsonType: numericTypes },
    MethodName: { bsonType: "string" },
    IsActive: { bsonType: "bool" }
  }
};
applyValidator('payment_methods', paymentMethodsSchema);

// 8) dchange_notifications (device-change notifications)
const dchangeSchema = {
  bsonType: "object",
  required: ["DChangeNotifyId","AppUserId","FullName","MobileNo","CreatedOn"],
  properties: {
    DChangeNotifyId: { bsonType: "string" },
    AppUserId: { bsonType: numericTypes },
    FullName: { bsonType: "string" },
    MobileNo: { bsonType: "string" },
    Address: { bsonType: "string" },
    MDeviceId: { bsonType: "string" },
    ProfileImage: { bsonType: ["string", "null"] },
    CreatedOn: { bsonType: "date" }
  }
};
applyValidator('dchange_notifications', dchangeSchema);

// 9) live_locations
const liveLocationsSchema = {
  bsonType: "object",
  required: ["AppUserId","Lat","Lon","Timestamp"],
  properties: {
    AppUserId: { bsonType: numericTypes },
    Lat: { bsonType: ["double","decimal"] },
    Lon: { bsonType: ["double","decimal"] },
    Timestamp: { bsonType: "date" }
  }
};
applyValidator('live_locations', liveLocationsSchema);

// Create helpful indexes (idempotent)
try {
  db.appusers.createIndex({ AppUserId: 1 }, { unique: true });
  db.appusers.createIndex({ MobileNo: 1 }, { unique: true });
  db.records.createIndex({ RecordId: 1 }, { unique: true });
  db.records.createIndex({ VehicleNo: 1 });
  db.finances.createIndex({ FinanceId: 1 }, { unique: true });
  db.confirmations.createIndex({ ConfirmationId: 1 }, { unique: true });
  db.feedbacks.createIndex({ FeedbackId: 1 }, { unique: true });
  db.payment_methods.createIndex({ PaymentMethodId: 1 }, { unique: true });
  db.dchange_notifications.createIndex({ DChangeNotifyId: 1 }, { unique: true });
  db.dchange_notifications.createIndex({ CreatedOn: -1 });
  db.live_locations.createIndex({ AppUserId: 1 });
  print('Indexes created/ensured.');
} catch (e) {
  print('Index creation error: ' + e);
}

// Lightweight seeding (only inserts if empty)
if (db.appusers.countDocuments({}) === 0) {
  db.appusers.insertMany([
    { AppUserId: 1, MobileNo: '9850637363', FirstName: 'Kunal', LastName: 'Admin', FullName: 'Kunal Admin', IsActive: true, IsAdmin: true, CreatedOn: new Date(Date.now() - 90*24*60*60*1000) },
    { AppUserId: 2, MobileNo: '9876543210', FirstName: 'Rahul', LastName: 'Sharma', FullName: 'Rahul Sharma', IsActive: true, IsAdmin: false, CreatedOn: new Date(Date.now() - 60*24*60*60*1000) },
    { AppUserId: 3, MobileNo: '9123456780', FirstName: 'Priya', LastName: 'Patel', FullName: 'Priya Patel', IsActive: true, IsAdmin: false, CreatedOn: new Date(Date.now() - 30*24*60*60*1000) }
  ]);
  print('Seeded appusers.');
} else print('appusers already has data - skipping seed.');

if (db.records.countDocuments({}) === 0) {
  const recs = [];
  for (let i = 1; i <= 8; i++) {
    recs.push({ RecordId: i, VehicleNo: `MH${String(i).padStart(2,'0')}-AB-${1000+i}`, OwnerName: `Owner ${i}`, ChassisNo: `CH${String(i).padStart(5,'0')}XYZ`, EngineNo: `EN${String(i).padStart(5,'0')}ABC`, BranchName: (i%2===0?'Pune':'Mumbai'), CreatedOn: new Date(Date.now() - i*15*24*60*60*1000) });
  }
  db.records.insertMany(recs);
  print('Seeded records.');
} else print('records already has data - skipping seed.');

if (db.finances.countDocuments({}) === 0) {
  const recs = db.records.find().toArray();
  const fins = [];
  for (let i = 1; i <= Math.min(5, recs.length); i++) {
    fins.push({ FinanceId: i, VehicleNo: recs[i-1].VehicleNo, FinanceType: (i%2===0?'Insurance':'Registration'), Amount: 5000 + i*1500, FinanceDate: new Date(Date.now() - i*30*24*60*60*1000), CreatedOn: new Date(Date.now() - i*30*24*60*60*1000) });
  }
  db.finances.insertMany(fins);
  print('Seeded finances.');
} else print('finances already has data - skipping seed.');

if (db.dchange_notifications.countDocuments({}) === 0) {
  db.dchange_notifications.insertMany([
    { DChangeNotifyId: 'dchg-1', AppUserId: 2, FullName: 'karan thorawade', MobileNo: '8055028206', Address: '55 budhwar peth karad415110', MDeviceId: 'md-1001', ProfileImage: null, CreatedOn: new Date('2026-05-03T14:05:00Z') },
    { DChangeNotifyId: 'dchg-2', AppUserId: 3, FullName: 'sajid khan', MobileNo: '8305732203', Address: 'BAJAR WARD NAGINA MASJID ...', MDeviceId: 'md-1002', ProfileImage: null, CreatedOn: new Date('2026-04-28T06:12:00Z') }
  ]);
  print('Seeded dchange_notifications.');
} else print('dchange_notifications already has data - skipping seed.');

print('vkenterprises schema + seed script finished.');
