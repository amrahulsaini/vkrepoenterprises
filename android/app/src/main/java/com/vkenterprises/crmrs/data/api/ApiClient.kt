package com.vkenterprises.crmrs.data.api

import com.vkenterprises.crmrs.BuildConfig
import okhttp3.ConnectionPool
import okhttp3.Dns
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.gson.GsonConverterFactory
import java.net.InetAddress
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit

object SessionTokens {
    @Volatile var tenantToken: String? = null
    @Volatile var agencySlug:  String? = null
    @Volatile var deviceId:    String? = null
}

private object CachingDns : Dns {
    private data class Entry(val addrs: List<InetAddress>, val expiry: Long)
    private val cache    = ConcurrentHashMap<String, Entry>()
    private val lastGood = ConcurrentHashMap<String, List<InetAddress>>()

    private val seed = mapOf("api.crmrecoverysoftware.com" to "103.67.239.102")

    override fun lookup(hostname: String): List<InetAddress> {
        val now = System.currentTimeMillis()
        cache[hostname]?.let { if (it.expiry > now) return it.addrs }
        return try {
            val addrs = Dns.SYSTEM.lookup(hostname)
            cache[hostname]    = Entry(addrs, now + 5 * 60 * 1000L)
            lastGood[hostname] = addrs
            addrs
        } catch (e: java.net.UnknownHostException) {
            lastGood[hostname]?.let { return it }
            cache[hostname]?.addrs?.let { return it }
            seed[hostname]?.let { ip -> return listOf(InetAddress.getByName(ip)) }
            throw e
        }
    }
}

object ApiClient {
    private val okHttp = OkHttpClient.Builder()
        .dns(CachingDns)
        .connectionPool(ConnectionPool(10, 3, TimeUnit.MINUTES))
        .dispatcher(okhttp3.Dispatcher().apply {
            maxRequests = 64
            maxRequestsPerHost = 24
        })
        .retryOnConnectionFailure(true)
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(60, TimeUnit.SECONDS)
        .writeTimeout(60, TimeUnit.SECONDS)
        .callTimeout(120, TimeUnit.SECONDS)
        .addInterceptor { chain ->
            val token    = SessionTokens.tenantToken
            val deviceId = SessionTokens.deviceId
            var builder  = chain.request().newBuilder()
            if (!token.isNullOrEmpty())    builder = builder.header("X-Tenant-Token", token)
            if (!deviceId.isNullOrEmpty()) builder = builder.header("X-Device-Id", deviceId)
            val request = builder.build()
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

    fun downloadBytes(url: String): ByteArray? = runCatching {
        val req = okhttp3.Request.Builder().url(url).get().build()
        okHttp.newCall(req).execute().use { resp ->
            if (resp.isSuccessful) resp.body?.bytes() else null
        }
    }.getOrNull()

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
