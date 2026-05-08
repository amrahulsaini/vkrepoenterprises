package com.vkenterprises.vras.data.repository

import com.vkenterprises.vras.data.api.ApiService
import com.vkenterprises.vras.data.local.BranchSyncState
import com.vkenterprises.vras.data.local.BranchSyncStateDao
import com.vkenterprises.vras.data.local.VehicleCache
import com.vkenterprises.vras.data.local.VehicleCacheDao
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class SyncRepository @Inject constructor(
    private val api: ApiService,
    private val vehicleDao: VehicleCacheDao,
    private val syncStateDao: BranchSyncStateDao
) {
    data class Progress(val current: Long, val total: Long, val done: Boolean = false)

    suspend fun hasLocalData(): Boolean = vehicleDao.count() > 0

    suspend fun sync(onProgress: suspend (Progress) -> Unit) {
        val branchResp = runCatching { api.getSyncBranches() }.getOrNull() ?: return
        if (!branchResp.isSuccessful) return
        val branches = branchResp.body()?.branches ?: return
        val totalRecords = branches.sumOf { it.totalRecords }

        var synced = 0L
        for (branch in branches) {
            if (branch.uploadedAt == null) continue
            val localState = syncStateDao.get(branch.branchId)
            if (localState?.uploadedAt == branch.uploadedAt) {
                synced += branch.totalRecords
                onProgress(Progress(synced, totalRecords))
                continue
            }

            vehicleDao.deleteByBranch(branch.branchId)

            var page = 0
            while (true) {
                val resp = runCatching { api.getSyncRecords(branch.branchId, page, 500) }.getOrNull() ?: break
                if (!resp.isSuccessful) break
                val body = resp.body() ?: break
                vehicleDao.insertAll(body.records.map { r ->
                    VehicleCache(r.id, branch.branchId, r.vehicleNo, r.chassisNo,
                        r.engineNo, r.model, r.customerName, r.last4, r.last5)
                })
                synced += body.records.size
                onProgress(Progress(synced, totalRecords))
                if (!body.hasMore) break
                page++
            }
            syncStateDao.save(BranchSyncState(branch.branchId, branch.uploadedAt))
        }
        onProgress(Progress(synced, totalRecords, done = true))
    }
}
