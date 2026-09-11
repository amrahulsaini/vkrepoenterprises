package com.vkenterprises.crmrs.workers

import android.content.Context
import androidx.hilt.work.HiltWorker
import androidx.work.*
import com.vkenterprises.crmrs.data.repository.SyncRepository
import dagger.assisted.Assisted
import dagger.assisted.AssistedInject

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

        return result
    }
}
