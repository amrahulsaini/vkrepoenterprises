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
        // Scheduling work touches WorkManager's internal database. Doing it on
        // the main thread here delays the very first frame (white screen on
        // launch). Run it on a background thread so onCreate() returns fast.
        Thread {
            runCatching {
                // FIRST thing on the background thread: open the TLS socket to
                // the API host so it's hot before the user can finish typing a
                // search. Kicks off DNS + handshake in parallel with the
                // WorkManager scheduling below.
                com.vkenterprises.vras.data.api.ApiClient.warmUp()
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

    // Start the self-chaining one-time sync immediately so background sync runs
    // every ~60s even without the ViewModel polling loop (REPLACE so it always
    // resets the 60s timer on app open rather than waiting for old chain to fire).
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
        // Cancel old 6-hour work if it exists from older installs
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
