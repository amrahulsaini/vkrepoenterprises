package com.vkenterprises.vras.data.repository

import com.vkenterprises.vras.data.api.ApiService
import com.vkenterprises.vras.data.local.BranchSyncState
import com.vkenterprises.vras.data.local.TenantDb
import com.vkenterprises.vras.data.local.VehicleCache
import com.vkenterprises.vras.data.models.SyncBranch
import kotlinx.coroutines.*
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import java.util.concurrent.atomic.AtomicLong
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class SyncRepository @Inject constructor(
    private val api: ApiService,
    private val db: TenantDb
) {
    private val vehicleDao   get() = db.vehicleCacheDao()
    private val syncStateDao get() = db.branchSyncStateDao()
    data class Progress(
        val current: Long,
        val total: Long,
        val done: Boolean = false,
        val started: Boolean = false
    )

    companion object {
        private const val PAGE_SIZE = 2000
    }

    suspend fun hasLocalData(): Boolean = vehicleDao.count() > 0

    suspend fun getSyncLogs(): List<BranchSyncState> = syncStateDao.getAll()

    suspend fun hasUpdates(): Boolean {
        val branchResp = runCatching { api.getSyncBranches() }.getOrNull() ?: return false
        if (!branchResp.isSuccessful) return false
        val branches = branchResp.body()?.branches ?: return false
        for (b in branches) {
            if (b.uploadedAt == null) continue
            val savedState = syncStateDao.get(b.branchId)
            if (savedState?.uploadedAt != b.uploadedAt) return true
        }
        return false
    }

    suspend fun forceSync(onProgress: suspend (Progress) -> Unit) {
        vehicleDao.deleteAll()
        syncStateDao.clearAll()
        sync(onProgress)
    }

    suspend fun sync(onProgress: suspend (Progress) -> Unit) {
        val branchResp = runCatching { api.getSyncBranches() }.getOrNull() ?: return
        if (!branchResp.isSuccessful) return
        val branches = branchResp.body()?.branches ?: return

        val serverIds = branches.map { it.branchId }.toSet()
        for (local in syncStateDao.getAll()) {
            if (local.branchId !in serverIds) {
                vehicleDao.deleteByBranch(local.branchId)
                syncStateDao.delete(local.branchId)
            }
        }

        val tasks = mutableListOf<SyncTask>()
        var totalToDownload = 0L

        for (b in branches) {
            if (b.uploadedAt == null) continue
            val savedState = syncStateDao.get(b.branchId)
            val localCount = vehicleDao.countByBranch(b.branchId)

            val uploadedChanged = savedState?.uploadedAt != b.uploadedAt
            val countMismatch   = localCount != b.totalRecords
            if (!uploadedChanged && !countMismatch) continue

            val fullReset  = uploadedChanged || localCount > b.totalRecords
            val startPage  = if (fullReset) 0 else (localCount / PAGE_SIZE).toInt()
            val toDownload = if (fullReset) b.totalRecords
                             else (b.totalRecords - startPage.toLong() * PAGE_SIZE).coerceAtLeast(PAGE_SIZE.toLong())

            tasks.add(SyncTask(b, fullReset, startPage, toDownload))
            totalToDownload += toDownload
        }

        if (tasks.isEmpty()) return
        onProgress(Progress(0L, totalToDownload.coerceAtLeast(1L), started = true))

        val synced = AtomicLong(0L)

        val gate = Semaphore(5)
        coroutineScope {
            tasks.map { task ->
                async(Dispatchers.IO) {
                    gate.withPermit {
                        downloadBranch(task, totalToDownload, synced, onProgress)
                    }
                }
            }.awaitAll()
        }

        onProgress(Progress(synced.get(), totalToDownload.coerceAtLeast(1L), done = true))
    }

    private suspend fun downloadBranch(
        task: SyncTask,
        totalToDownload: Long,
        synced: AtomicLong,
        onProgress: suspend (Progress) -> Unit
    ) {
        val branch = task.branch

        if (task.fullReset) vehicleDao.deleteByBranch(branch.branchId)

        syncStateDao.save(BranchSyncState(branch.branchId, branch.uploadedAt!!))

        var page = task.startPage
        while (true) {
            val resp = runCatching {
                api.getSyncRecords(branch.branchId, page, PAGE_SIZE)
            }.getOrNull() ?: break
            if (!resp.isSuccessful) break
            val body = resp.body() ?: break

            vehicleDao.insertAll(body.records.map { r ->
                VehicleCache(r.id, branch.branchId, r.vehicleNo, r.chassisNo,
                    r.engineNo, r.model, r.customerName, r.last4, r.last5)
            })
            synced.addAndGet(body.records.size.toLong())
            onProgress(Progress(synced.get(), totalToDownload.coerceAtLeast(1L)))

            if (!body.hasMore) break
            page++
        }
    }
}

private data class SyncTask(
    val branch: SyncBranch,
    val fullReset: Boolean,
    val startPage: Int,
    val toDownload: Long
)
