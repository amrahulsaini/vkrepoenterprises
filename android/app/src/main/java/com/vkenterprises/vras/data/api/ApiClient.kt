package com.vkenterprises.vras.data.api

import com.vkenterprises.vras.BuildConfig
import okhttp3.ConnectionPool
import okhttp3.Dns
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.gson.GsonConverterFactory
import java.net.InetAddress
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit

// Holds the signed tenant token and the current agency slug in memory so the
// OkHttp interceptor and the per-tenant Room database (both synchronous) can
// read them without touching DataStore. Populated at app start from
// PreferencesManager and refreshed on every login / logout.
object SessionTokens {
    @Volatile var tenantToken: String? = null
    // Drives which  vk_cache_<slug>.db  file the local Room database opens —
    // see TenantDb. Switching this swaps the offline-records database.
    @Volatile var agencySlug:  String? = null
}

// Simple in-process DNS cache — avoids a system DNS round-trip on every search.
// Entries live for 5 minutes; after that the next request re-resolves naturally.
private object CachingDns : Dns {
    private data class Entry(val addrs: List<InetAddress>, val expiry: Long)
    private val cache = ConcurrentHashMap<String, Entry>()
    override fun lookup(hostname: String): List<InetAddress> {
        val now = System.currentTimeMillis()
        cache[hostname]?.let { if (it.expiry > now) return it.addrs }
        val addrs = Dns.SYSTEM.lookup(hostname)
        cache[hostname] = Entry(addrs, now + 5 * 60 * 1000L)
        return addrs
    }
}

object ApiClient {
    private val okHttp = OkHttpClient.Builder()
        .dns(CachingDns)
        // Keep up to 10 alive connections for 3 min so every search after the first
        // skips the TCP+TLS handshake entirely.
        .connectionPool(ConnectionPool(10, 3, TimeUnit.MINUTES))
        .retryOnConnectionFailure(true)
        .connectTimeout(10, TimeUnit.SECONDS)
        .readTimeout(15, TimeUnit.SECONDS)
        .writeTimeout(15, TimeUnit.SECONDS)
        .callTimeout(20, TimeUnit.SECONDS)
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
