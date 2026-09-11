package com.vkenterprises.crmrs

import android.app.Application
import androidx.hilt.work.HiltWorkerFactory
import androidx.work.*
import com.vkenterprises.crmrs.data.api.SessionTokens
import com.vkenterprises.crmrs.utils.DeviceIdUtil
import com.vkenterprises.crmrs.workers.LocationWorker
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
        SessionTokens.deviceId = DeviceIdUtil.get(this)
        Thread {
            runCatching {
                com.vkenterprises.crmrs.data.api.ApiClient.warmUp()
                stopBackgroundSync()
                scheduleLocationWork()
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

    private fun stopBackgroundSync() {
        val wm = WorkManager.getInstance(this)
        wm.cancelUniqueWork("vehicle_sync")
        wm.cancelUniqueWork("vehicle_sync_v2")
        wm.cancelUniqueWork("vehicle_sync_chain")
    }
}
