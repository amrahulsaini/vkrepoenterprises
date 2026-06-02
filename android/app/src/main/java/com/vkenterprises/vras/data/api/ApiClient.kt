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
        // OkHttp's Dispatcher defaults to maxRequestsPerHost = 5. EVERY call
        // (search, heartbeat, sync, status) goes to the SAME API host, so the
        // background traffic — a heartbeat every few seconds plus the 60s sync
        // poll — could occupy all 5 slots and QUEUE a user's search behind them.
        // A queued search just spins showing nothing: the "fast search lags /
        // shows no result after a day or two" bug. Raising the per-host cap
        // guarantees a user-initiated search always gets its own slot instead of
        // waiting behind chatter.
        .dispatcher(okhttp3.Dispatcher().apply {
            maxRequests = 64
            maxRequestsPerHost = 24
        })
        .retryOnConnectionFailure(true)
        // Bumped to cover the register call, which uploads up to ~1 MB of
        // compressed JPEGs (PFP + 3 KYC docs). A 20s ceiling timed out on
        // slow connections. Read-only calls (search etc) still finish in
        // <100ms thanks to OkHttp's connection pool reuse.
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(60, TimeUnit.SECONDS)
        .writeTimeout(60, TimeUnit.SECONDS)
        .callTimeout(120, TimeUnit.SECONDS)
        // Routes every request to the signed-in agency's database server-side.
        .addInterceptor { chain ->
            val token = SessionTokens.tenantToken
            val request =
                if (!token.isNullOrEmpty())
                    chain.request().newBuilder()
                        .header("X-Tenant-Token", token)
                        .build()
                else chain.request()
            // The heartbeat is a tiny, frequent, fire-and-forget call. Never let
            // a stalled one squat on a per-host connection slot (and a server DB
            // connection) for the full 120s callTimeout — bound its connect/read
            // tightly so a flaky network fails it fast and frees the slot for the
            // next search. Other calls keep the generous timeouts (register
            // uploads ~1 MB of JPEGs).
            if (request.url.encodedPath.endsWith("/heartbeat")) {
                chain.withConnectTimeout(8, TimeUnit.SECONDS)
                    .withReadTimeout(8, TimeUnit.SECONDS)
                    .withWriteTimeout(8, TimeUnit.SECONDS)
                    .proceed(request)
            } else {
                chain.proceed(request)
            }
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

    // Pre-opens the TCP + TLS connection to the API host so the user's FIRST
    // search reuses a hot socket instead of paying the ~300-600ms DNS +
    // TLS-handshake cost on a cold connection. THIS is what made the first
    // online search feel slow; subsequent searches were already fast because
    // OkHttp's connection pool kept the socket alive. Mirrors the desktop
    // app's App() warm-up (App.xaml.cs). Best-effort and fully async — any
    // failure (and any non-200 like a 404/405 on the bare base URL) is
    // ignored, because the only thing we want is the negotiated socket left
    // sitting in the pool. The 60s sync-poll then keeps it warm for the rest
    // of the session, so search never hits a cold connection again.
    fun warmUp() {
        runCatching {
            val req = okhttp3.Request.Builder().url(BuildConfig.BASE_URL).get().build()
            okHttp.newCall(req).enqueue(object : okhttp3.Callback {
                override fun onFailure(call: okhttp3.Call, e: java.io.IOException) {}
                override fun onResponse(call: okhttp3.Call, response: okhttp3.Response) {
                    response.close()
                }
            })
        }
    }
}
