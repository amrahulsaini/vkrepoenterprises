package com.vkenterprises.vras.navigation

sealed class Screen(val route: String) {
    object Splash              : Screen("splash")
    object Login               : Screen("login")
    object Register            : Screen("register")
    object WaitingApproval     : Screen("waiting_approval/{reason}") {
        fun go(reason: String) = "waiting_approval/$reason"
    }
    object Home                : Screen("home")
    object VehicleDetail       : Screen("vehicle_detail")
    object SubscriptionExpired : Screen("subscription_expired")
    object Profile             : Screen("profile")
}
