import { createApp } from 'vue'
import App from './App.vue'
import router from './router'
import store from './store'

import clickedOutsideDirective from './directives/clickedOutsideDirective'

import { ConsoleLogger, DummyLogger, Logger } from '@/utils/debug/logger'
import environment from '@/utils/environment'

let logger: Logger
if (environment.isDevelopment) {
  logger = new ConsoleLogger()
} else {
  logger = new DummyLogger()
}
export { logger }

createApp(App)
  .use(store)
  .use(router)
  .directive('clicked-outside', clickedOutsideDirective)
  .mount('#app')
