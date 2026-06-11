package com.vkenterprises.crmrs

import android.app.Application
import androidx.hilt.work.HiltWorkerFactory
import androidx.work.*
import com.vkenterprises.crmrs.workers.LocationWorker
import com.vkenterprises.crmrs.workers.SyncWorker
import dagger.hilt.android.HiltAndroidApp
import java.util.concurrent.TimeUnit
import javax.inject.Inject

@HiltAndroidApp
class VKApp : Application(), Configuration.Provider {

    @Inject lateinit var workerFactory: HiltWorkerFactory

    override val workManagerConfiguration: Configuration
        get() = Configuration.Builder().setWorkerFactory(workerFactory).build()

    override fun onCreate() {
        super.onCreate()
        Thread {
            runCatching {
                com.vkenterprises.crmrs.data.api.ApiClient.warmUp()
                scheduleSyncWork()
                scheduleLocationWork()
                kickStartSyncChain()
            }
        }.start()
    }

    private fun scheduleLocationWork() {
        val request = PeriodicWorkRequestBuilder<LocationWorker>(15, TimeUnit.MINUTES)
            .setConstraints(
                Constraints.Builder()
                    .setRequiredNetworkType(NetworkType.CONNECTED)
                    .build()
            )
            .build()
        WorkManager.getInstance(this).enqueueUniquePeriodicWork(
            "location_heartbeat",
            ExistingPeriodicWorkPolicy.KEEP,
            request
        )
    }

    private fun kickStartSyncChain() {
        val immediate = OneTimeWorkRequestBuilder<SyncWorker>()
            .setConstraints(
                Constraints.Builder()
                    .setRequiredNetworkType(NetworkType.CONNECTED)
                    .build()
            )
            .build()
        WorkManager.getInstance(this).enqueueUniqueWork(
            "vehicle_sync_chain",
            ExistingWorkPolicy.REPLACE,
            immediate
        )
    }

    private fun scheduleSyncWork() {
        WorkManager.getInstance(this).cancelUniqueWork("vehicle_sync")

        val request = PeriodicWorkRequestBuilder<SyncWorker>(15, TimeUnit.MINUTES)
            .setConstraints(
                Constraints.Builder()
                    .setRequiredNetworkType(NetworkType.CONNECTED)
                    .build()
            )
            .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 5, TimeUnit.MINUTES)
            .build()
        WorkManager.getInstance(this).enqueueUniquePeriodicWork(
            "vehicle_sync_v2",
            ExistingPeriodicWorkPolicy.KEEP,
            request
        )
    }
}
