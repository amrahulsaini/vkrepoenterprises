package com.vkenterprises.crmrs.utils

import java.io.IOException
import java.net.ConnectException
import java.net.SocketTimeoutException
import java.net.UnknownHostException
import java.util.concurrent.TimeoutException
import javax.net.ssl.SSLException

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
