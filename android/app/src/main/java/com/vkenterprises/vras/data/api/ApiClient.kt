package com.vkenterprises.vras.data.api

import com.vkenterprises.vras.BuildConfig
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.gson.GsonConverterFactory
import java.util.concurrent.TimeUnit

// Holds the signed tenant token in memory so the OkHttp interceptor (which
// must run synchronously) can attach it without touching DataStore. Populated
// at app start from PreferencesManager and refreshed on every login.
object SessionTokens {
    @Volatile var tenantToken: String? = null
}

object ApiClient {
    private val okHttp = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .writeTimeout(30, TimeUnit.SECONDS)
        // Hard cap on the whole call so a stalled request can never hang forever.
        .callTimeout(90, TimeUnit.SECONDS)
        // Routes every request to the signed-in agency's database server-side.
        .addInterceptor { chain ->
            val token = SessionTokens.tenantToken
            val request =
                if (!token.isNullOrEmpty())
                    chain.request().newBuilder()
                        .header("X-Tenant-Token", token)
                        .build()
                else chain.request()
            chain.proceed(request)
        }
        .addInterceptor(HttpLoggingInterceptor().apply {
            // CRITICAL: never log full bodies in a release build. BODY logging
            // buffers every entire request+response into memory and a string —
            // the sync payloads are large, so this caused random memory
            // pressure / out-of-memory white screens. Bodies only in debug.
            level = if (BuildConfig.DEBUG) HttpLoggingInterceptor.Level.BASIC
                    else HttpLoggingInterceptor.Level.NONE
        })
        .build()

    val api: ApiService = Retrofit.Builder()
        .baseUrl(BuildConfig.BASE_URL)
        .client(okHttp)
        .addConverterFactory(GsonConverterFactory.create())
        .build()
        .create(ApiService::class.java)
}
