package com.vkenterprises.vras.data.local

import androidx.room.*

// ── Entity ───────────────────────────────────────────────────────────────────
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
    val last5: String
)

@Entity(tableName = "branch_sync_state")
data class BranchSyncState(
    @PrimaryKey val branchId: Int,
    val uploadedAt: String
)

// ── DAOs ─────────────────────────────────────────────────────────────────────
@Dao
interface VehicleCacheDao {
    @Query("SELECT * FROM vehicle_cache WHERE last4 = :q ORDER BY vehicleNo LIMIT 500")
    suspend fun searchByLast4(q: String): List<VehicleCache>

    @Query("SELECT * FROM vehicle_cache WHERE last5 = :q ORDER BY chassisNo LIMIT 500")
    suspend fun searchByLast5(q: String): List<VehicleCache>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(records: List<VehicleCache>)

    @Query("DELETE FROM vehicle_cache WHERE branchId = :branchId")
    suspend fun deleteByBranch(branchId: Int)

    @Query("SELECT COUNT(*) FROM vehicle_cache")
    suspend fun count(): Long
}

@Dao
interface BranchSyncStateDao {
    @Query("SELECT * FROM branch_sync_state WHERE branchId = :id")
    suspend fun get(id: Int): BranchSyncState?

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun save(state: BranchSyncState)
}

// ── Database ──────────────────────────────────────────────────────────────────
@Database(
    entities = [VehicleCache::class, BranchSyncState::class],
    version = 1,
    exportSchema = false
)
abstract class VKDatabase : RoomDatabase() {
    abstract fun vehicleCacheDao(): VehicleCacheDao
    abstract fun branchSyncStateDao(): BranchSyncStateDao
}
