package com.vkenterprises.crmrs.data.local

import androidx.room.*
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase

@Entity(
    tableName = "vehicle_cache",
    indices = [
        Index(value = ["last4"]),
        Index(value = ["last5"]),
        Index(value = ["branchId"])
    ]
)
data class VehicleCache(
    @PrimaryKey val id: Long,
    val branchId: Int,
    val vehicleNo: String,
    val chassisNo: String,
    val engineNo: String,
    val model: String,
    val customerName: String,
    val last4: String,
    val last5: String,
    val agreementNo: String = "",
    val customerContact: String = "",
    val customerAddress: String = "",
    val region: String = "",
    val area: String = "",
    val bucket: String = "",
    val gv: String = "",
    val od: String = "",
    val seasoning: String = "",
    val tbrFlag: String = "",
    val sec9: String = "",
    val sec17: String = "",
    val level1: String = "",
    val level1Contact: String = "",
    val level2: String = "",
    val level2Contact: String = "",
    val level3: String = "",
    val level3Contact: String = "",
    val level4: String = "",
    val level4Contact: String = "",
    val senderMail1: String = "",
    val senderMail2: String = "",
    val executiveName: String = "",
    val pos: String = "",
    val toss: String = "",
    val remark: String = "",
    val branchFromExcel: String = "",
    val createdOn: String = ""
)

@Entity(tableName = "branch_sync_state")
data class BranchSyncState(
    @PrimaryKey val branchId: Int,
    val uploadedAt: String,
    val branchName: String = "",
    val financerName: String = "",
    val contact1: String = "",
    val contact2: String = "",
    val contact3: String = "",
    val address: String = ""
)

@Dao
interface VehicleCacheDao {
    @Query("SELECT * FROM vehicle_cache WHERE last4 = :q ORDER BY vehicleNo")
    suspend fun searchByLast4(q: String): List<VehicleCache>

    @Query("SELECT * FROM vehicle_cache WHERE last5 = :q ORDER BY chassisNo")
    suspend fun searchByLast5(q: String): List<VehicleCache>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(records: List<VehicleCache>)

    @Query("DELETE FROM vehicle_cache WHERE branchId = :branchId")
    suspend fun deleteByBranch(branchId: Int)

    @Query("SELECT COUNT(*) FROM vehicle_cache")
    suspend fun count(): Long

    @Query("SELECT COUNT(*) FROM vehicle_cache WHERE branchId = :branchId")
    suspend fun countByBranch(branchId: Int): Long

    @Query("DELETE FROM vehicle_cache")
    suspend fun deleteAll()
}

@Dao
interface BranchSyncStateDao {
    @Query("SELECT * FROM branch_sync_state WHERE branchId = :id")
    suspend fun get(id: Int): BranchSyncState?

    @Query("SELECT * FROM branch_sync_state")
    suspend fun getAll(): List<BranchSyncState>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun save(state: BranchSyncState)

    @Query("DELETE FROM branch_sync_state WHERE branchId = :id")
    suspend fun delete(id: Int)

    @Query("DELETE FROM branch_sync_state")
    suspend fun clearAll()
}

@Database(
    entities = [VehicleCache::class, BranchSyncState::class],
    version = 3,
    exportSchema = false
)
abstract class VKDatabase : RoomDatabase() {
    abstract fun vehicleCacheDao(): VehicleCacheDao
    abstract fun branchSyncStateDao(): BranchSyncStateDao
}

val MIGRATION_1_2 = object : Migration(1, 2) {
    override fun migrate(db: SupportSQLiteDatabase) {
        db.execSQL("ALTER TABLE branch_sync_state ADD COLUMN branchName TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE branch_sync_state ADD COLUMN financerName TEXT NOT NULL DEFAULT ''")
    }
}

val MIGRATION_2_3 = object : Migration(2, 3) {
    override fun migrate(db: SupportSQLiteDatabase) {
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN agreementNo TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN customerContact TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN customerAddress TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN region TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN area TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN bucket TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN gv TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN od TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN seasoning TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN tbrFlag TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN sec9 TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN sec17 TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN level1 TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN level1Contact TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN level2 TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN level2Contact TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN level3 TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN level3Contact TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN level4 TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN level4Contact TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN senderMail1 TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN senderMail2 TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN executiveName TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN pos TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN toss TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN remark TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN branchFromExcel TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE vehicle_cache ADD COLUMN createdOn TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE branch_sync_state ADD COLUMN contact1 TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE branch_sync_state ADD COLUMN contact2 TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE branch_sync_state ADD COLUMN contact3 TEXT NOT NULL DEFAULT ''")
        db.execSQL("ALTER TABLE branch_sync_state ADD COLUMN address TEXT NOT NULL DEFAULT ''")
    }
}
