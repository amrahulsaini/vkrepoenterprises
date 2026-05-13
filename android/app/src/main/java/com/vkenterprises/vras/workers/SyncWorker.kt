package com.vkenterprises.vras.workers

import android.content.Context
import androidx.hilt.work.HiltWorker
import androidx.work.*
import com.vkenterprises.vras.data.repository.SyncRepository
import dagger.assisted.Assisted
import dagger.assisted.AssistedInject
import java.util.concurrent.TimeUnit

@HiltWorker
class SyncWorker @AssistedInject constructor(
    @Assisted context: Context,
    @Assisted params: WorkerParameters,
    private val syncRepo: SyncRepository
) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result {
        val result = runCatching {
            syncRepo.sync { }
            Result.success()
        }.getOrElse {
            if (runAttemptCount < 2) Result.retry() else Result.failure()
        }

        // Self-chain: schedule the next run 60 seconds from now.
        // This gives ~60s background sync even when the app process is alive but
        // the ViewModel polling loop isn't running. The periodic 15-min work in
        // VKApp is a safety net for when this chain breaks (process killed, reboot).
        if (result == Result.success()) {
            val next = OneTimeWorkRequestBuilder<SyncWorker>()
                .setInitialDelay(60, TimeUnit.SECONDS)
                .setConstraints(
                    Constraints.Builder()
                        .setRequiredNetworkType(NetworkType.CONNECTED)
                        .build()
                )
                .build()
            WorkManager.getInstance(applicationContext).enqueueUniqueWork(
                "vehicle_sync_chain",
                ExistingWorkPolicy.REPLACE,
                next
            )
        }

        return result
    }
}
