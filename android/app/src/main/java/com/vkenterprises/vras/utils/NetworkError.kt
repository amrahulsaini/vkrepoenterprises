package com.vkenterprises.vras.utils

import java.io.IOException
import java.net.ConnectException
import java.net.SocketTimeoutException
import java.net.UnknownHostException
import java.util.concurrent.TimeoutException
import javax.net.ssl.SSLException

// Turns a raw exception into a clean, production-grade message for the user.
// We NEVER show internal text like "Unable to resolve host
// api.crmrecoverysoftware.com" or stack/host details — those scare users and
// leak infrastructure. Instead we say, in plain language, that it's a
// connection problem on their side and to try again.
object NetworkError {
    fun friendly(t: Throwable?): String = when (t) {
        is UnknownHostException ->
            "No internet connection. Please check your device's network and try again."
        is SocketTimeoutException, is TimeoutException ->
            "The connection timed out. Please check your network and try again."
        is ConnectException ->
            "Couldn't connect. Please check your internet connection and try again."
        is SSLException ->
            "Secure connection failed. Please check your network and try again."
        is IOException ->
            "Network problem on your device. Please check your connection and try again."
        else ->
            "Something went wrong. Please try again."
    }
}
