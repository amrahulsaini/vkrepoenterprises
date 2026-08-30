package com.vkenterprises.crmrs.data.repository

import com.vkenterprises.crmrs.data.api.ApiService
import com.vkenterprises.crmrs.data.local.BranchSyncState
import com.vkenterprises.crmrs.data.local.TenantDb
import com.vkenterprises.crmrs.data.local.VehicleCache
import com.vkenterprises.crmrs.data.models.SyncBranch
import com.vkenterprises.crmrs.data.models.SyncRecordsResponse
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
        private const val PAGE_SIZE     = 2000
        private const val PAGE_RETRIES  = 3
        private const val RETRY_DELAY_MS = 1500L
    }

    suspend fun hasLocalData(): Boolean = vehicleDao.count() > 0

    suspend fun getSyncLogs(): List<BranchSyncState> = syncStateDao.getAll()

    suspend fun getCachedBranches(): List<BranchSyncState> = syncStateDao.getAll()

    suspend fun hasUpdates(): Boolean {
        val localStates = runCatching { syncStateDao.getAll() }.getOrDefault(emptyList())
        if (localStates.any { !it.completed }) return true

        val branchResp = runCatching { api.getSyncBranches() }.getOrNull()
            ?: return localStates.isEmpty()
        if (!branchResp.isSuccessful) return localStates.isEmpty()
        val branches = branchResp.body()?.branches ?: return localStates.isEmpty()

        val byId = localStates.associateBy { it.branchId }
        for (b in branches) {
            if (b.uploadedAt == null) continue
            val savedState = byId[b.branchId]
            if (savedState == null || savedState.uploadedAt != b.uploadedAt) return true
            if (!savedState.completed) return true
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

        for (b in branches) {
            if (b.uploadedAt == null) continue
            val existing = syncStateDao.get(b.branchId)
            if (existing != null && (
                    existing.branchName   != b.branchName   ||
                    existing.financerName != b.financerName ||
                    existing.contact1     != b.contact1     ||
                    existing.contact2     != b.contact2     ||
                    existing.contact3     != b.contact3     ||
                    existing.address      != b.address)) {
                syncStateDao.save(existing.copy(
                    branchName   = b.branchName,
                    financerName = b.financerName,
                    contact1     = b.contact1,
                    contact2     = b.contact2,
                    contact3     = b.contact3,
                    address      = b.address
                ))
            }
        }

        val allStates = syncStateDao.getAll()
        val serverIds = branches.map { it.branchId }.toSet()
        for (local in allStates) {
            if (local.branchId !in serverIds) {
                vehicleDao.deleteByBranch(local.branchId)
                syncStateDao.delete(local.branchId)
            }
        }

        val stateById = allStates.associateBy { it.branchId }
        val countById = vehicleDao.countPerBranch().associate { it.branchId to it.cnt }

        val serverTotal = branches.filter { it.uploadedAt != null }.sumOf { it.totalRecords }
        val alreadyHave = countById.values.sum()

        val tasks = mutableListOf<SyncTask>()

        for (b in branches) {
            if (b.uploadedAt == null) continue
            val savedState = stateById[b.branchId]
            val localCount = countById[b.branchId] ?: 0L

            val uploadedChanged = savedState?.uploadedAt != b.uploadedAt
            val incomplete      = savedState?.completed != true
            if (!uploadedChanged && !incomplete) continue

            val fullReset  = uploadedChanged || localCount > b.totalRecords
            val startPage  = if (fullReset) 0 else (localCount / PAGE_SIZE).toInt()

            tasks.add(SyncTask(b, fullReset, startPage, 0L))
        }

        if (tasks.isEmpty()) return

        val resetCount = tasks.filter { it.fullReset }
            .sumOf { countById[it.branch.branchId] ?: 0L }
        val baseline = (alreadyHave - resetCount).coerceAtLeast(0L)
        val total    = serverTotal.coerceAtLeast(1L)

        onProgress(Progress(baseline, total, started = true))

        val synced = AtomicLong(baseline)

        val gate = Semaphore(5)
        coroutineScope {
            tasks.map { task ->
                async(Dispatchers.IO) {
                    gate.withPermit {
                        downloadBranch(task, total, synced, onProgress)
                    }
                }
            }.awaitAll()
        }

        onProgress(Progress(synced.get().coerceAtMost(total), total, done = true))
    }

    private suspend fun downloadBranch(
        task: SyncTask,
        totalToDownload: Long,
        synced: AtomicLong,
        onProgress: suspend (Progress) -> Unit
    ) {
        val branch = task.branch

        if (task.fullReset) vehicleDao.deleteByBranch(branch.branchId)

        fun state(done: Boolean) = BranchSyncState(
            branch.branchId, branch.uploadedAt!!, branch.branchName, branch.financerName,
            branch.contact1, branch.contact2, branch.contact3, branch.address, done
        )

        syncStateDao.save(state(false))

        var page = task.startPage
        var finished = false
        while (true) {
            val body = fetchPage(branch.branchId, page) ?: break

            vehicleDao.insertAll(body.records.map { r ->
                VehicleCache(r.id, branch.branchId, r.vehicleNo, r.chassisNo,
                    r.engineNo, r.model, r.customerName, r.last4, r.last5,
                    r.agreementNo, r.customerContact, r.customerAddress, r.region, r.area, r.bucket, r.gv, r.od, r.seasoning, r.tbrFlag, r.sec9, r.sec17, r.level1, r.level1Contact, r.level2, r.level2Contact, r.level3, r.level3Contact, r.level4, r.level4Contact, r.senderMail1, r.senderMail2, r.executiveName, r.pos, r.toss, r.remark, r.branchFromExcel, r.createdOn)
            })
            synced.addAndGet(body.records.size.toLong())
            onProgress(Progress(synced.get().coerceAtMost(totalToDownload), totalToDownload))

            if (!body.hasMore) { finished = true; break }
            page++
        }

        if (finished) syncStateDao.save(state(true))
    }

    private suspend fun fetchPage(branchId: Int, page: Int): SyncRecordsResponse? {
        var attempt = 0
        while (attempt < PAGE_RETRIES) {
            val resp = runCatching { api.getSyncRecords(branchId, page, PAGE_SIZE) }.getOrNull()
            val body = if (resp != null && resp.isSuccessful) resp.body() else null
            if (body != null) return body
            attempt++
            if (attempt < PAGE_RETRIES) delay(RETRY_DELAY_MS * attempt)
        }
        return null
    }
}

private data class SyncTask(
    val branch: SyncBranch,
    val fullReset: Boolean,
    val startPage: Int,
    val toDownload: Long
)
