import { LitElement, html, css, property, customElement } from 'lit-element';

/**
 * Lit-element that can be dragged and dropped. Needs a <tasklist> parent
 * and a <section> grandparent. Place a <placeholder> element inside the section
 * and style it as you wish.
 */
@customElement('draggable-element')
export class Draggable extends LitElement {
   private placeholder = "placeholder";
   private mouseIsDown = false;
   private startPos = {X: 0, Y: 0};
   private mouseDownPos = {X: 0, Y: 0};
   private detached = false;
   private lastHoveredDraggable;

   static get styles() {
      return css`
      :host {
        display: block;
        -moz-user-select: none;
        user-select: none;
      }
    `;
   }

   render() {
      return html`<slot></slot>`;
   }

   firstUpdated(changedProperties) {
      let dragger = this.querySelector(".dragger");
      if (dragger == undefined) dragger = this;
      else dragger.addEventListener("click", () => this.mouseIsDown = false); // otherwise it won't let go when you click

      dragger.addEventListener('mousedown', e => this.onMouseDown(e));
      this.addEventListener('click', e => this.onClick(e));
      document.body.addEventListener('mouseup', (e) => {
         if (this.detached) this.onMouseUp(this, e);
      });
      document.body.addEventListener('mousemove', e => {
         if (this.mouseIsDown) this.onMouseMove(e);
      });
      this.addEventListener("mousedown", e => { // Save mouse coordinates when mouse down anywhere on element
         this.mouseDownPos = {
            X: e.clientX,
            Y: e.clientY
         };
      });
   }

   onMouseDown(e) {
      this.mouseIsDown = true;
      this.startPos = {
         X: e.clientX - this.getBoundingClientRect().left,
         Y: e.clientY - this.getBoundingClientRect().top
      };
   }

   onClick(e) {
      // If mouse is at same position as before, it's just a click.
      // Fire the "draggableClick" event, since a normal click event also fires
      // even when the element has been dragged.
      if (this.mouseDownPos.X == e.clientX &&
         this.mouseDownPos.Y == e.clientY) {
         this.dispatchEvent(new CustomEvent("draggableClick"));
      }
   }

   onMouseUp(element, e) {
      element.mouseIsDown = false;

      // Attach element
      element.style.position = "";
      element.style.width = "";
      element.style.top = "";
      element.style.left = "";

      // Move to placeholder
      const placeholder: HTMLElement = element.parentElement.parentElement.querySelector(this.placeholder);
      let taskListIndex = Array.from(placeholder.parentNode.parentNode.children)
         .indexOf(placeholder.parentElement);
      element.parentElement.children[taskListIndex]
         .insertBefore(element, placeholder);
      placeholder.style.display = "none";
      element.detached = false;
   }

   onMouseMove(e) {
      // Detach from list
      if (!this.detached) {
         const computedStyle = getComputedStyle(this);
         // Placeholder
         const placeholder: HTMLElement = this.parentElement.parentElement.querySelector(this.placeholder);
         this.parentElement.insertBefore(placeholder, this); // Move placeholder to list slot
         Object.assign(placeholder.style, {
            width: computedStyle.width,
            height: computedStyle.height,
            display: "block"
         });

         // Draggable
         this.parentElement.parentElement.appendChild(this); // Move task out from tasklists, then get position: fixed
         this.style.width = computedStyle.width;
         this.style.position = "fixed";
         this.detached = true;
      }

      const parentRect = this.parentElement.parentElement.getBoundingClientRect();
      const newPos = {
         X: e.clientX - this.startPos.X - parentRect.left,
         Y: e.clientY - this.startPos.Y - parentRect.top
      }

      this.style.left = newPos.X + "px";
      this.style.top = newPos.Y + "px";

      /// Show where item will be dropped ///
      const elementsUnder = this.getRelatedElementsUnder();
      const placeholder: HTMLElement = this.parentElement.parentElement.querySelector(this.placeholder);
      const tasklistElements = elementsUnder.tasklist.children;
      let lastRect = undefined;
      if (tasklistElements.length > 0 )
         lastRect = tasklistElements[tasklistElements.length - 1].getBoundingClientRect(); // Get last element in tasklist if not empty
      if (elementsUnder.tasklist == undefined) return;

      // If a draggable is under, and the placeholder wasn't already inserted there
      if (elementsUnder.closestDraggable != undefined && elementsUnder.closestDraggable != this) {
         const overTopHalf = elementsUnder.middlePoint.Y <= this.getMiddlePoint(elementsUnder.closestDraggable).Y;

         // If over the top half of the element
         if  (overTopHalf && this.lastHoveredDraggable != elementsUnder.closestDraggable) {
            elementsUnder.tasklist.insertBefore(placeholder, elementsUnder.closestDraggable);
            this.lastHoveredDraggable = elementsUnder.closestDraggable; // Remember last placement
         } else if (this.lastHoveredDraggable != elementsUnder.closestDraggable.nextSibling) { // If over the bottom half of the element
            elementsUnder.tasklist.insertBefore(placeholder, elementsUnder.closestDraggable.nextSibling);
            this.lastHoveredDraggable = elementsUnder.closestDraggable.nextSibling;
         }
      } else if (lastRect == undefined) { // If empty tasklist
         elementsUnder.tasklist.appendChild(placeholder);
      } else if (this.getMiddlePoint().Y > lastRect.top + lastRect.height) {  // If at bottom
         elementsUnder.tasklist.appendChild(placeholder);
      }
   }

   /**
    * Get <draggable> element under the element currently being dragged, and 
    * also the hovered tasklist.
    */
   private getRelatedElementsUnder() {
      const middlePoint = this.getMiddlePoint();
      const elementsOnPoint = document.elementsFromPoint(middlePoint.X, middlePoint.Y);
      let   closestDraggable = elementsOnPoint.filter(x => x.tagName == "DRAGGABLE-ELEMENT")[1];
      const tasklist = elementsOnPoint.filter(x => x.tagName == "TASKLIST")[0];

      if (tasklist != undefined && closestDraggable == undefined) {
         const under = document.elementsFromPoint(middlePoint.X, middlePoint.Y + 40)
            .filter(x => x.tagName == "DRAGGABLE-ELEMENT");

         if (under[1] == undefined) closestDraggable = under[0];
         else closestDraggable = under[1];
      }

      return { closestDraggable, tasklist, middlePoint };
   }

   /**
    * Get the global coordinates of the elements middle point.
    * @param   {element} element The element to get the middle point of
    */
   private getMiddlePoint(element: Element = this) {
      const rect = element.getBoundingClientRect();
      return {
         X: rect.width / 2 + rect.left,
         Y: rect.height / 2 + rect.top
      };
   }
}
