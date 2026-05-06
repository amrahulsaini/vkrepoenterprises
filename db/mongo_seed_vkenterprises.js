// MongoDB seed script for vkenterprises
// Run with:
// mongosh "mongodb://localhost:27017/vkenterprises" --file db/mongo_seed_vkenterprises.js

print('vkenterprises seed script starting...');
print('DB: ' + db.getName());

function ensureCollection(name) {
  if (!db.getCollectionNames().includes(name)) {
    db.createCollection(name);
    print('Created collection: ' + name);
  } else {
    print('Collection exists: ' + name);
  }
}

const collections = [
  'appusers',
  'records',
  'finances',
  'confirmations',
  'feedbacks',
  'payment_methods',
  'dchange_notifications',
  'branches',
  'live_locations'
];

collections.forEach(ensureCollection);

// Create indexes (idempotent)
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
  print('Indexes ensured.');
} catch (ixErr) {
  print('Index creation error: ' + ixErr);
}

// Seed data only if collections are empty
const now = new Date();

if (db.appusers.countDocuments({}) === 0) {
  db.appusers.insertMany([
    { AppUserId: 1, MobileNo: '9850637363', FirstName: 'Kunal', LastName: 'Admin', FullName: 'Kunal Admin', IsActive: true, IsAdmin: true, CreatedOn: new Date(now.getTime() - 90*24*60*60*1000) },
    { AppUserId: 2, MobileNo: '9876543210', FirstName: 'Rahul', LastName: 'Sharma', FullName: 'Rahul Sharma', IsActive: true, IsAdmin: false, CreatedOn: new Date(now.getTime() - 60*24*60*60*1000) },
    { AppUserId: 3, MobileNo: '9123456780', FirstName: 'Priya', LastName: 'Patel', FullName: 'Priya Patel', IsActive: true, IsAdmin: false, CreatedOn: new Date(now.getTime() - 30*24*60*60*1000) }
  ]);
  print('Seeded appusers');
} else print('appusers not empty -- skipping seed');

// Records
if (db.records.countDocuments({}) === 0) {
  const records = [];
  for (let i = 1; i <= 8; i++) {
    records.push({
      RecordId: i,
      VehicleNo: `MH${String(i).padStart(2,'0')}-AB-${1000 + i}`,
      OwnerName: `Owner ${i}`,
      ChassisNo: `CH${String(i).padStart(5,'0')}XYZ`,
      EngineNo: `EN${String(i).padStart(5,'0')}ABC`,
      BranchName: (i % 2 === 0) ? 'Pune' : 'Mumbai',
      CreatedOn: new Date(now.getTime() - i * 15 * 24 * 60 * 60 * 1000)
    });
  }
  db.records.insertMany(records);
  print('Seeded records');
} else print('records not empty -- skipping seed');

// Finances
if (db.finances.countDocuments({}) === 0) {
  const recs = db.records.find().toArray();
  const finances = [];
  for (let i = 1; i <= Math.min(5, recs.length); i++) {
    finances.push({
      FinanceId: i,
      VehicleNo: recs[i-1].VehicleNo,
      FinanceType: (i % 2 === 0) ? 'Insurance' : 'Registration',
      Amount: 5000 + i * 1500,
      FinanceDate: new Date(now.getTime() - i * 30 * 24 * 60 * 60 * 1000),
      CreatedOn: new Date(now.getTime() - i * 30 * 24 * 60 * 60 * 1000)
    });
  }
  db.finances.insertMany(finances);
  print('Seeded finances');
} else print('finances not empty -- skipping seed');

// Confirmations
if (db.confirmations.countDocuments({}) === 0) {
  const recs = db.records.find().toArray();
  db.confirmations.insertMany([
    { ConfirmationId: 1, VehicleNo: recs.length>0 ? recs[0].VehicleNo : 'MH01-AB-1001', Status: 'Approved', AppUserId: 1, CreatedOn: new Date(now.getTime() - 2*24*60*60*1000) },
    { ConfirmationId: 2, VehicleNo: recs.length>1 ? recs[1].VehicleNo : 'MH02-AB-1002', Status: 'Pending', AppUserId: 2, CreatedOn: new Date(now.getTime() - 1*24*60*60*1000) }
  ]);
  print('Seeded confirmations');
} else print('confirmations not empty -- skipping seed');

// Feedbacks
if (db.feedbacks.countDocuments({}) === 0) {
  db.feedbacks.insertMany([
    { FeedbackId: 1, AppUserId: 2, Subject: 'App is great!', Status: 'Open', CreatedOn: new Date(now.getTime() - 5*24*60*60*1000) },
    { FeedbackId: 2, AppUserId: 3, Subject: 'Need more features', Status: 'Open', CreatedOn: new Date(now.getTime() - 2*24*60*60*1000) }
  ]);
  print('Seeded feedbacks');
} else print('feedbacks not empty -- skipping seed');

// Payment methods
if (db.payment_methods.countDocuments({}) === 0) {
  db.payment_methods.insertMany([
    { PaymentMethodId: 1, MethodName: 'Cash', IsActive: true },
    { PaymentMethodId: 2, MethodName: 'UPI', IsActive: true },
    { PaymentMethodId: 3, MethodName: 'Card', IsActive: true },
    { PaymentMethodId: 4, MethodName: 'Net Banking', IsActive: false }
  ]);
  print('Seeded payment_methods');
} else print('payment_methods not empty -- skipping seed');

// Device-change notifications (DChangeNotifyDetails)
if (db.dchange_notifications.countDocuments({}) === 0) {
  db.dchange_notifications.insertMany([
    { DChangeNotifyId: 'dchg-1', AppUserId: 2, FullName: 'karan thorawade', MobileNo: '8055028206', Address: '55 budhwar peth karad415110', MDeviceId: 'md-1001', ProfileImage: null, CreatedOn: new Date('2026-05-03T14:05:00Z') },
    { DChangeNotifyId: 'dchg-2', AppUserId: 3, FullName: 'sajid khan', MobileNo: '8305732203', Address: 'BAJAR WARD NAGINA MASJID ...', MDeviceId: 'md-1002', ProfileImage: null, CreatedOn: new Date('2026-04-28T06:12:00Z') }
  ]);
  print('Seeded dchange_notifications');
} else print('dchange_notifications not empty -- skipping seed');

// Branches (optional)
if (db.branches.countDocuments({}) === 0) {
  db.branches.insertMany([
    { BranchId: 1, Name: 'Mumbai', City: 'Mumbai' },
    { BranchId: 2, Name: 'Pune', City: 'Pune' }
  ]);
  print('Seeded branches');
} else print('branches not empty -- skipping seed');

// Live locations (example)
if (db.live_locations.countDocuments({}) === 0) {
  db.live_locations.insertMany([
    { AppUserId: 2, Lat: 18.5204, Lon: 73.8567, Timestamp: new Date(now.getTime() - 60*60*1000) },
    { AppUserId: 3, Lat: 19.0760, Lon: 72.8777, Timestamp: new Date(now.getTime() - 30*60*1000) }
  ]);
  print('Seeded live_locations');
} else print('live_locations not empty -- skipping seed');

print('vkenterprises seed script finished.');
