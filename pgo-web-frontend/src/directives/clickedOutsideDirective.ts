/* eslint-disable @typescript-eslint/no-explicit-any */
import { Directive } from 'vue'

// Trigger a function when a click is registered outside the element
const clickedOutsideDirective: Directive = {
  mounted(element: any, binding) {
    const clickEventHandler = (event: MouseEvent) => {
      if (element !== event.target
          && !element.contains(event.target)) {
        binding.value(event)
      }
    }
    element.__clickedOutsideHandler__ = clickEventHandler
    document.addEventListener("click", clickEventHandler)
  },
  unmounted(element) {
    document.removeEventListener("click", element.__clickedOutsideHandler__)
  },
}

export default clickedOutsideDirective
