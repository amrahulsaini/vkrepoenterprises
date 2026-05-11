package com.vkenterprises.vras.workers

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import androidx.core.content.ContextCompat
import androidx.hilt.work.HiltWorker
import androidx.work.*
import com.google.android.gms.location.LocationServices
import com.google.android.gms.location.Priority
import com.google.android.gms.tasks.CancellationTokenSource
import com.vkenterprises.vras.data.api.ApiService
import com.vkenterprises.vras.data.models.HeartbeatRequest
import com.vkenterprises.vras.utils.PreferencesManager
import dagger.assisted.Assisted
import dagger.assisted.AssistedInject
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.tasks.await

@HiltWorker
class LocationWorker @AssistedInject constructor(
    @Assisted private val context: Context,
    @Assisted params: WorkerParameters,
    private val api: ApiService,
    private val prefs: PreferencesManager
) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result {
        val userId = prefs.userId.first()
        if (userId <= 0) return Result.success()

        var lat: Double? = null
        var lng: Double? = null

        val fineGranted = ContextCompat.checkSelfPermission(
            context, Manifest.permission.ACCESS_FINE_LOCATION
        ) == PackageManager.PERMISSION_GRANTED
        val coarseGranted = ContextCompat.checkSelfPermission(
            context, Manifest.permission.ACCESS_COARSE_LOCATION
        ) == PackageManager.PERMISSION_GRANTED

        if (fineGranted || coarseGranted) {
            runCatching {
                val client = LocationServices.getFusedLocationProviderClient(context)
                val cts    = CancellationTokenSource()
                val loc    = client.getCurrentLocation(
                    if (fineGranted) Priority.PRIORITY_BALANCED_POWER_ACCURACY
                    else Priority.PRIORITY_LOW_POWER,
                    cts.token
                ).await()
                if (loc != null) {
                    lat = loc.latitude
                    lng = loc.longitude
                }
            }
        }

        runCatching {
            api.heartbeat(HeartbeatRequest(userId, lat, lng))
        }

        return Result.success()
    }
}
