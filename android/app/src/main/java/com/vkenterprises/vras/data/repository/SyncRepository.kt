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
    // started=true  → actual download is beginning, show the banner
    // done=true     → all done, hide the banner
    // otherwise     → progress update while downloading
    data class Progress(
        val current: Long,
        val total: Long,
        val done: Boolean = false,
        val started: Boolean = false
    )

    suspend fun hasLocalData(): Boolean = vehicleDao.count() > 0

    suspend fun sync(onProgress: suspend (Progress) -> Unit) {
        val branchResp = runCatching { api.getSyncBranches() }.getOrNull() ?: return
        if (!branchResp.isSuccessful) return
        val branches = branchResp.body()?.branches ?: return

        // Prune local data for branches no longer on the server (cleared/deleted)
        val serverIds = branches.map { it.branchId }.toSet()
        for (local in syncStateDao.getAll()) {
            if (local.branchId !in serverIds) {
                vehicleDao.deleteByBranch(local.branchId)
                syncStateDao.delete(local.branchId)
            }
        }

        // Find only branches that actually changed since last sync
        val toSync = branches.filter { b ->
            b.uploadedAt != null &&
            syncStateDao.get(b.branchId)?.uploadedAt != b.uploadedAt
        }
        if (toSync.isEmpty()) return  // Nothing changed — no progress callback, no banner

        val totalToSync = toSync.sumOf { it.totalRecords }
        onProgress(Progress(0L, totalToSync, started = true))  // Signal: show banner now

        var synced = 0L
        for (branch in toSync) {
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
                onProgress(Progress(synced, totalToSync))
                if (!body.hasMore) break
                page++
            }
            syncStateDao.save(BranchSyncState(branch.branchId, branch.uploadedAt!!))
        }
        onProgress(Progress(synced, totalToSync, done = true))
    }
}
