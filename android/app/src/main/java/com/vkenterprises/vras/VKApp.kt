package com.vkenterprises.vras

import android.app.Application
import androidx.hilt.work.HiltWorkerFactory
import androidx.work.*
import com.vkenterprises.vras.workers.LocationWorker
import com.vkenterprises.vras.workers.SyncWorker
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
        scheduleSyncWork()
        scheduleLocationWork()
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

    private fun scheduleSyncWork() {
        val request = PeriodicWorkRequestBuilder<SyncWorker>(6, TimeUnit.HOURS)
            .setConstraints(
                Constraints.Builder()
                    .setRequiredNetworkType(NetworkType.CONNECTED)
                    .build()
            )
            .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 15, TimeUnit.MINUTES)
            .build()
        WorkManager.getInstance(this).enqueueUniquePeriodicWork(
            "vehicle_sync",
            ExistingPeriodicWorkPolicy.KEEP,
            request
        )
    }
}
